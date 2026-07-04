using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;

namespace VividRP.Runtime.ECS
{
    internal delegate bool VividEcsManagerJobEnabledDelegate<TContext>(TContext context);

    internal delegate JobHandle VividEcsManagerJobScheduleDelegate<TContext>(
        TContext context,
        JobHandle dependency);

    internal interface IVividEcsManagerJobModuleFlags
    {
        uint EnabledModuleFlags { get; }
    }

    internal enum VividEcsPageDispatchMode
    {
        Dynamic = 0,
        Average = 1,
    }

    internal interface IVividEcsPageJob
    {
        void Execute(VividEcsPageInfo page);
    }

    internal static class VividEcsPageJobExtensions
    {
        private static readonly ProfilerMarker s_ScheduleMarker = new("VividRP.ECS.IJobPage.Schedule");

        public static JobHandle Schedule<TJob>(
            this TJob jobData,
            NativeArray<VividEcsPageInfo> pages,
            JobHandle dependency = default)
            where TJob : struct, IVividEcsPageJob
        {
            if (!pages.IsCreated || pages.Length == 0)
                return dependency;

            using (s_ScheduleMarker.Auto())
            {
                var wrapper = new VividEcsPageJobWrapper<TJob>
                {
                    Pages = pages,
                    JobData = jobData,
                };
                return wrapper.Schedule(pages.Length, 1, dependency);
            }
        }

        public static JobHandle ScheduleParallel<TJob>(
            this TJob jobData,
            NativeArray<VividEcsPageInfo> pages,
            JobHandle dependency = default,
            int innerloopBatchCount = 1,
            VividEcsPageDispatchMode dispatchMode = VividEcsPageDispatchMode.Average)
            where TJob : struct, IVividEcsPageJob
        {
            if (!pages.IsCreated || pages.Length == 0)
                return dependency;

            int batchCount = math.max(1, innerloopBatchCount);
            if (dispatchMode == VividEcsPageDispatchMode.Average)
            {
                int workerCount = math.max(JobsUtility.JobWorkerCount, 1) + 1;
                batchCount = math.max(1, (pages.Length + workerCount - 1) / workerCount);
            }

            using (s_ScheduleMarker.Auto())
            {
                var wrapper = new VividEcsPageJobWrapper<TJob>
                {
                    Pages = pages,
                    JobData = jobData,
                };
                return wrapper.Schedule(pages.Length, batchCount, dependency);
            }
        }
    }

    [BurstCompile]
    internal struct VividEcsPageJobWrapper<TJob> : IJobParallelFor
        where TJob : struct, IVividEcsPageJob
    {
        [ReadOnly]
        public NativeArray<VividEcsPageInfo> Pages;

        public TJob JobData;

        public void Execute(int index)
        {
            VividEcsPageInfo page = Pages[index];
            if (page.EntryCount > 0)
                JobData.Execute(page);
        }
    }

    internal readonly struct VividEcsPageGroupInfo
    {
        public VividEcsPageGroupInfo(NativeArray<VividEcsPageInfo> pages, int startIndex, int pageCount)
        {
            Pages = pages;
            StartIndex = startIndex;
            PageCount = pageCount;
        }

        public NativeArray<VividEcsPageInfo> Pages { get; }

        public int StartIndex { get; }

        public int PageCount { get; }

        public VividEcsPageInfo this[int index] => Pages[StartIndex + index];
    }

    internal interface IVividEcsPageGroupJob
    {
        void Execute(VividEcsPageGroupInfo pageGroup);
    }

    internal static class VividEcsPageGroupJobExtensions
    {
        private static readonly ProfilerMarker s_ScheduleMarker = new("VividRP.ECS.IJobPageGroup.Schedule");

        public static JobHandle Schedule<TJob>(
            this TJob jobData,
            NativeArray<VividEcsPageInfo> pages,
            NativeArray<int2> pageGroups,
            JobHandle dependency = default)
            where TJob : struct, IVividEcsPageGroupJob
        {
            if (!pages.IsCreated || !pageGroups.IsCreated || pageGroups.Length == 0)
                return dependency;

            using (s_ScheduleMarker.Auto())
            {
                var wrapper = new VividEcsPageGroupJobWrapper<TJob>
                {
                    Pages = pages,
                    PageGroups = pageGroups,
                    JobData = jobData,
                };
                return wrapper.Schedule(pageGroups.Length, 1, dependency);
            }
        }

        public static JobHandle ScheduleParallel<TJob>(
            this TJob jobData,
            NativeArray<VividEcsPageInfo> pages,
            NativeArray<int2> pageGroups,
            JobHandle dependency = default,
            int innerloopBatchCount = 1)
            where TJob : struct, IVividEcsPageGroupJob
        {
            if (!pages.IsCreated || !pageGroups.IsCreated || pageGroups.Length == 0)
                return dependency;

            using (s_ScheduleMarker.Auto())
            {
                var wrapper = new VividEcsPageGroupJobWrapper<TJob>
                {
                    Pages = pages,
                    PageGroups = pageGroups,
                    JobData = jobData,
                };
                return wrapper.Schedule(pageGroups.Length, math.max(1, innerloopBatchCount), dependency);
            }
        }
    }

    [BurstCompile]
    internal struct VividEcsPageGroupJobWrapper<TJob> : IJobParallelFor
        where TJob : struct, IVividEcsPageGroupJob
    {
        [ReadOnly]
        public NativeArray<VividEcsPageInfo> Pages;

        [ReadOnly]
        public NativeArray<int2> PageGroups;

        public TJob JobData;

        public void Execute(int index)
        {
            int2 range = PageGroups[index];
            if (range.y > 0)
                JobData.Execute(new VividEcsPageGroupInfo(Pages, range.x, range.y));
        }
    }

    internal sealed class VividEcsManagerJobRegistry<TContext>
    {
        private readonly List<Entry> m_Entries = new();
        private int m_NextId;
        private bool m_Sorted = true;

        public int count => m_Entries.Count;

        public int Register(
            string name,
            int order,
            VividEcsManagerJobScheduleDelegate<TContext> schedule,
            VividEcsManagerJobEnabledDelegate<TContext> enabled = null,
            uint requiredModuleFlags = 0u)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));

            int id = ++m_NextId;
            m_Entries.Add(new Entry(id, name ?? string.Empty, order, requiredModuleFlags, enabled, schedule));
            m_Sorted = false;
            return id;
        }

        public int RegisterModule(
            string name,
            int order,
            uint requiredModuleFlags,
            VividEcsManagerJobScheduleDelegate<TContext> schedule,
            VividEcsManagerJobEnabledDelegate<TContext> enabled = null)
        {
            return Register(name, order, schedule, enabled, requiredModuleFlags);
        }

        public bool Unregister(int id)
        {
            for (int index = 0; index < m_Entries.Count; index++)
            {
                if (m_Entries[index].Id != id)
                    continue;

                m_Entries.RemoveAt(index);
                return true;
            }

            return false;
        }

        public int EnabledCount(TContext context)
        {
            return EnabledCount(context, ResolveModuleFlags(context));
        }

        public int EnabledCount(TContext context, uint enabledModuleFlags)
        {
            int count = 0;
            for (int index = 0; index < m_Entries.Count; index++)
            {
                if (m_Entries[index].IsEnabled(context, enabledModuleFlags))
                    count++;
            }

            return count;
        }

        public JobHandle ScheduleEnabled(TContext context, JobHandle dependency = default)
        {
            return ScheduleEnabled(context, ResolveModuleFlags(context), dependency);
        }

        public JobHandle ScheduleEnabled(
            TContext context,
            uint enabledModuleFlags,
            JobHandle dependency = default)
        {
            SortIfNeeded();
            JobHandle handle = dependency;
            for (int index = 0; index < m_Entries.Count; index++)
            {
                Entry entry = m_Entries[index];
                if (entry.IsEnabled(context, enabledModuleFlags))
                    handle = entry.Schedule(context, handle);
            }

            return handle;
        }

        public void Clear()
        {
            m_Entries.Clear();
            m_NextId = 0;
            m_Sorted = true;
        }

        private void SortIfNeeded()
        {
            if (m_Sorted)
                return;

            m_Entries.Sort((left, right) =>
            {
                int orderCompare = left.Order.CompareTo(right.Order);
                return orderCompare != 0 ? orderCompare : left.Id.CompareTo(right.Id);
            });
            m_Sorted = true;
        }

        private static uint ResolveModuleFlags(TContext context)
        {
            return context is IVividEcsManagerJobModuleFlags moduleFlags
                ? moduleFlags.EnabledModuleFlags
                : 0u;
        }

        private readonly struct Entry
        {
            public Entry(
                int id,
                string name,
                int order,
                uint requiredModuleFlags,
                VividEcsManagerJobEnabledDelegate<TContext> enabled,
                VividEcsManagerJobScheduleDelegate<TContext> schedule)
            {
                Id = id;
                Name = name;
                Order = order;
                RequiredModuleFlags = requiredModuleFlags;
                Enabled = enabled;
                ScheduleDelegate = schedule;
            }

            public readonly int Id;
            public readonly string Name;
            public readonly int Order;
            public readonly uint RequiredModuleFlags;
            private readonly VividEcsManagerJobEnabledDelegate<TContext> Enabled;
            private readonly VividEcsManagerJobScheduleDelegate<TContext> ScheduleDelegate;

            public bool IsEnabled(TContext context, uint enabledModuleFlags)
            {
                if (RequiredModuleFlags != 0u)
                {
                    if ((enabledModuleFlags & RequiredModuleFlags) != RequiredModuleFlags)
                        return false;
                }

                return Enabled == null || Enabled(context);
            }

            public JobHandle Schedule(TContext context, JobHandle dependency)
            {
                return ScheduleDelegate(context, dependency);
            }
        }
    }
}
