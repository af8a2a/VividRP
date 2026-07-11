using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

    [JobProducerType(typeof(VividEcsPageJobExtensions.VividEcsPageJobProducer<>))]
    internal interface IVividEcsPageJob
    {
        void Execute(VividEcsPageInfo page, int pageIndex);
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
            return ScheduleInternal(
                ref jobData,
                new NativeSlice<VividEcsPageInfo>(pages),
                dependency,
                innerloopBatchCount: 1,
                isParallel: false,
                VividEcsPageDispatchMode.Dynamic);
        }

        public static JobHandle ScheduleByRef<TJob>(
            this ref TJob jobData,
            NativeArray<VividEcsPageInfo> pages,
            JobHandle dependency = default)
            where TJob : struct, IVividEcsPageJob
        {
            return ScheduleInternal(
                ref jobData,
                new NativeSlice<VividEcsPageInfo>(pages),
                dependency,
                innerloopBatchCount: 1,
                isParallel: false,
                VividEcsPageDispatchMode.Dynamic);
        }

        public static JobHandle ScheduleParallel<TJob>(
            this TJob jobData,
            NativeArray<VividEcsPageInfo> pages,
            JobHandle dependency = default,
            int innerloopBatchCount = 1,
            VividEcsPageDispatchMode dispatchMode = VividEcsPageDispatchMode.Average)
            where TJob : struct, IVividEcsPageJob
        {
            return ScheduleInternal(
                ref jobData,
                new NativeSlice<VividEcsPageInfo>(pages),
                dependency,
                innerloopBatchCount,
                isParallel: true,
                dispatchMode);
        }

        public static JobHandle ScheduleParallelByRef<TJob>(
            this ref TJob jobData,
            NativeArray<VividEcsPageInfo> pages,
            JobHandle dependency = default,
            int innerloopBatchCount = 1,
            VividEcsPageDispatchMode dispatchMode = VividEcsPageDispatchMode.Average)
            where TJob : struct, IVividEcsPageJob
        {
            return ScheduleInternal(
                ref jobData,
                new NativeSlice<VividEcsPageInfo>(pages),
                dependency,
                innerloopBatchCount,
                isParallel: true,
                dispatchMode);
        }

        public static JobHandle ScheduleParallelEmbedded<TJob, TWork>(
            this TJob jobData,
            NativeArray<TWork> pageWorks,
            int pageInfoByteOffset,
            JobHandle dependency = default,
            int innerloopBatchCount = 1,
            VividEcsPageDispatchMode dispatchMode = VividEcsPageDispatchMode.Average)
            where TJob : struct, IVividEcsPageJob
            where TWork : unmanaged
        {
            NativeSlice<VividEcsPageInfo> pages = CreateEmbeddedPageSlice(pageWorks, pageInfoByteOffset);
            return ScheduleInternal(
                ref jobData,
                pages,
                dependency,
                innerloopBatchCount,
                isParallel: true,
                dispatchMode);
        }

        public static JobHandle ScheduleParallelEmbeddedByRef<TJob, TWork>(
            this ref TJob jobData,
            NativeArray<TWork> pageWorks,
            int pageInfoByteOffset,
            JobHandle dependency = default,
            int innerloopBatchCount = 1,
            VividEcsPageDispatchMode dispatchMode = VividEcsPageDispatchMode.Average)
            where TJob : struct, IVividEcsPageJob
            where TWork : unmanaged
        {
            NativeSlice<VividEcsPageInfo> pages = CreateEmbeddedPageSlice(pageWorks, pageInfoByteOffset);
            return ScheduleInternal(
                ref jobData,
                pages,
                dependency,
                innerloopBatchCount,
                isParallel: true,
                dispatchMode);
        }

        public static void EarlyJobInit<TJob>()
            where TJob : struct, IVividEcsPageJob
        {
            VividEcsPageJobProducer<TJob>.Initialize();
        }

        private static unsafe JobHandle ScheduleInternal<TJob>(
            ref TJob jobData,
            NativeSlice<VividEcsPageInfo> pages,
            JobHandle dependency,
            int innerloopBatchCount,
            bool isParallel,
            VividEcsPageDispatchMode dispatchMode)
            where TJob : struct, IVividEcsPageJob
        {
            if (pages.Length == 0)
                return dependency;

            int batchCount = math.max(1, innerloopBatchCount);
            if (isParallel && dispatchMode == VividEcsPageDispatchMode.Average)
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
                    IsParallel = isParallel ? 1 : 0,
                };
                var scheduleParameters = new JobsUtility.JobScheduleParameters(
                    UnsafeUtility.AddressOf(ref wrapper),
                    GetReflectionData<TJob>(),
                    dependency,
                    isParallel ? ScheduleMode.Parallel : ScheduleMode.Single);
                return isParallel
                    ? JobsUtility.ScheduleParallelFor(ref scheduleParameters, pages.Length, batchCount)
                    : JobsUtility.Schedule(ref scheduleParameters);
            }
        }

        private static unsafe NativeSlice<VividEcsPageInfo> CreateEmbeddedPageSlice<TWork>(
            NativeArray<TWork> pageWorks,
            int pageInfoByteOffset)
            where TWork : unmanaged
        {
            if (!pageWorks.IsCreated || pageWorks.Length == 0)
                return default;

            int workStride = UnsafeUtility.SizeOf<TWork>();
            int pageInfoSize = UnsafeUtility.SizeOf<VividEcsPageInfo>();
            if (pageInfoByteOffset < 0 || pageInfoByteOffset + pageInfoSize > workStride)
                throw new ArgumentOutOfRangeException(nameof(pageInfoByteOffset));

            byte* pageData = (byte*)pageWorks.GetUnsafeReadOnlyPtr() + pageInfoByteOffset;
            NativeSlice<VividEcsPageInfo> pages = NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<VividEcsPageInfo>(
                pageData,
                workStride,
                pageWorks.Length);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeSliceUnsafeUtility.SetAtomicSafetyHandle(
                ref pages,
                NativeArrayUnsafeUtility.GetAtomicSafetyHandle(pageWorks));
#endif
            return pages;
        }

        private static IntPtr GetReflectionData<TJob>()
            where TJob : struct, IVividEcsPageJob
        {
            VividEcsPageJobProducer<TJob>.Initialize();
            IntPtr reflectionData = VividEcsPageJobProducer<TJob>.ReflectionData.Data;
            CollectionHelper.CheckReflectionDataCorrect<TJob>(reflectionData);
            return reflectionData;
        }

        internal struct VividEcsPageJobWrapper<TJob>
            where TJob : struct, IVividEcsPageJob
        {
            [ReadOnly]
            public NativeSlice<VividEcsPageInfo> Pages;

            public TJob JobData;

            public int IsParallel;
        }

        internal struct VividEcsPageJobProducer<TJob>
            where TJob : struct, IVividEcsPageJob
        {
            internal static readonly SharedStatic<IntPtr> ReflectionData =
                SharedStatic<IntPtr>.GetOrCreate<VividEcsPageJobProducer<TJob>>();

            [BurstDiscard]
            internal static void Initialize()
            {
                if (ReflectionData.Data == IntPtr.Zero)
                {
                    ReflectionData.Data = JobsUtility.CreateJobReflectionData(
                        typeof(VividEcsPageJobWrapper<TJob>),
                        typeof(TJob),
                        (ExecuteJobFunction)Execute);
                }
            }

            internal delegate void ExecuteJobFunction(
                ref VividEcsPageJobWrapper<TJob> wrapper,
                IntPtr additionalPtr,
                IntPtr bufferRangePatchData,
                ref JobRanges ranges,
                int jobIndex);

            public static unsafe void Execute(
                ref VividEcsPageJobWrapper<TJob> wrapper,
                IntPtr additionalPtr,
                IntPtr bufferRangePatchData,
                ref JobRanges ranges,
                int jobIndex)
            {
                bool isParallel = wrapper.IsParallel != 0;
                while (true)
                {
                    int beginPageIndex = 0;
                    int endPageIndex = wrapper.Pages.Length;
                    if (isParallel
                        && !JobsUtility.GetWorkStealingRange(
                            ref ranges,
                            jobIndex,
                            out beginPageIndex,
                            out endPageIndex))
                    {
                        return;
                    }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    JobsUtility.PatchBufferMinMaxRanges(
                        bufferRangePatchData,
                        UnsafeUtility.AddressOf(ref wrapper),
                        beginPageIndex,
                        endPageIndex - beginPageIndex);
#endif

                    for (int pageIndex = beginPageIndex; pageIndex < endPageIndex; pageIndex++)
                    {
                        VividEcsPageInfo page = wrapper.Pages[pageIndex];
                        if (page.EntryCount > 0)
                            wrapper.JobData.Execute(page, pageIndex);
                    }

                    if (!isParallel)
                        return;
                }
            }
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

        public JobHandle ScheduleEnabledParallel(TContext context, JobHandle dependency = default)
        {
            return ScheduleEnabledParallel(context, ResolveModuleFlags(context), dependency);
        }

        public JobHandle ScheduleEnabledParallel(
            TContext context,
            uint enabledModuleFlags,
            JobHandle dependency = default)
        {
            SortIfNeeded();
            JobHandle combinedHandle = dependency;
            bool hasScheduledJob = false;
            for (int index = 0; index < m_Entries.Count; index++)
            {
                Entry entry = m_Entries[index];
                if (!entry.IsEnabled(context, enabledModuleFlags))
                    continue;

                JobHandle handle = entry.Schedule(context, dependency);
                combinedHandle = hasScheduledJob
                    ? JobHandle.CombineDependencies(combinedHandle, handle)
                    : handle;
                hasScheduledJob = true;
            }

            return hasScheduledJob ? combinedHandle : dependency;
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
