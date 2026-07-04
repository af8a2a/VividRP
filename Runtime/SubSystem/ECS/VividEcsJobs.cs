using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;

namespace VividRP.Runtime.ECS
{
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
}
