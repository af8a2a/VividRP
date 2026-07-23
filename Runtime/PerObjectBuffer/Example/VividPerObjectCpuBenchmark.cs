using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace VividRP.Runtime.Examples
{
    public readonly struct VividPerObjectCpuBenchmarkCase
    {
        internal VividPerObjectCpuBenchmarkCase(
            string name,
            string operationUnit,
            long operationCount,
            long elapsedTicks,
            long allocatedBytes)
        {
            Name = name;
            OperationUnit = operationUnit;
            OperationCount = operationCount;
            TotalMilliseconds = elapsedTicks * 1000.0 / Stopwatch.Frequency;
            AllocatedBytes = allocatedBytes;
        }

        public string Name { get; }

        public string OperationUnit { get; }

        public long OperationCount { get; }

        public double TotalMilliseconds { get; }

        public long AllocatedBytes { get; }

        public double NanosecondsPerOperation =>
            OperationCount > 0
                ? TotalMilliseconds * 1_000_000.0 / OperationCount
                : 0.0;

        public double AllocatedBytesPerOperation =>
            OperationCount > 0
                ? AllocatedBytes / (double)OperationCount
                : 0.0;
    }

    public sealed class VividPerObjectCpuBenchmarkReport
    {
        internal VividPerObjectCpuBenchmarkReport(
            int rendererCount,
            int warmupIterations,
            int measurementIterations,
            VividPerObjectColorExampleController.PropertyAccessMode perObjectAccessMode,
            VividPerObjectCpuBenchmarkCase materialPropertyBlockChanging,
            VividPerObjectCpuBenchmarkCase perObjectBufferChanging,
            VividPerObjectCpuBenchmarkCase perObjectBufferChangingSubmit,
            VividPerObjectCpuBenchmarkCase materialPropertyBlockUnchanged,
            VividPerObjectCpuBenchmarkCase perObjectBufferUnchanged,
            VividPerObjectCpuBenchmarkCase perObjectBufferUnchangedSubmit)
        {
            RendererCount = rendererCount;
            WarmupIterations = warmupIterations;
            MeasurementIterations = measurementIterations;
            PerObjectAccessMode = perObjectAccessMode;
            MaterialPropertyBlockChanging = materialPropertyBlockChanging;
            PerObjectBufferChanging = perObjectBufferChanging;
            PerObjectBufferChangingSubmit = perObjectBufferChangingSubmit;
            MaterialPropertyBlockUnchanged = materialPropertyBlockUnchanged;
            PerObjectBufferUnchanged = perObjectBufferUnchanged;
            PerObjectBufferUnchangedSubmit = perObjectBufferUnchangedSubmit;
        }

        public int RendererCount { get; }

        public int WarmupIterations { get; }

        public int MeasurementIterations { get; }

        public VividPerObjectColorExampleController.PropertyAccessMode PerObjectAccessMode { get; }

        public VividPerObjectCpuBenchmarkCase MaterialPropertyBlockChanging { get; }

        public VividPerObjectCpuBenchmarkCase PerObjectBufferChanging { get; }

        public VividPerObjectCpuBenchmarkCase PerObjectBufferChangingSubmit { get; }

        public VividPerObjectCpuBenchmarkCase MaterialPropertyBlockUnchanged { get; }

        public VividPerObjectCpuBenchmarkCase PerObjectBufferUnchanged { get; }

        public VividPerObjectCpuBenchmarkCase PerObjectBufferUnchangedSubmit { get; }

        public double ChangingWriteSpeedup =>
            Divide(
                MaterialPropertyBlockChanging.NanosecondsPerOperation,
                PerObjectBufferChanging.NanosecondsPerOperation);

        public double UnchangedWriteSpeedup =>
            Divide(
                MaterialPropertyBlockUnchanged.NanosecondsPerOperation,
                PerObjectBufferUnchanged.NanosecondsPerOperation);

        public override string ToString()
        {
            var builder = new StringBuilder(1024);
            builder.AppendLine("VividRP Per-Object CPU write benchmark");
            builder.Append("Renderers: ").Append(RendererCount)
                .Append(", warmup frames: ").Append(WarmupIterations)
                .Append(", measured frames: ").Append(MeasurementIterations)
                .Append(", SSBO access: ").AppendLine(PerObjectAccessMode.ToString());
            builder.AppendLine();
            builder.AppendLine("Case                                      Total ms    ns/unit    B/unit   Unit");
            AppendCase(builder, MaterialPropertyBlockChanging);
            AppendCase(builder, PerObjectBufferChanging);
            AppendCase(builder, PerObjectBufferChangingSubmit);
            AppendCase(builder, MaterialPropertyBlockUnchanged);
            AppendCase(builder, PerObjectBufferUnchanged);
            AppendCase(builder, PerObjectBufferUnchangedSubmit);
            builder.AppendLine();
            builder.Append("Changing write speedup (MPB / SSBO): ")
                .Append(ChangingWriteSpeedup.ToString("F2", CultureInfo.InvariantCulture))
                .AppendLine("x");
            builder.Append("Unchanged write speedup (MPB / SSBO): ")
                .Append(UnchangedWriteSpeedup.ToString("F2", CultureInfo.InvariantCulture))
                .AppendLine("x");
            builder.AppendLine(
                "Write rows measure only the per-Renderer API calls. SSBO submit rows separately " +
                "measure PrepareAndBind plus Graphics.ExecuteCommandBuffer once per simulated frame.");
            builder.AppendLine(
                "Use the Unity Profiler markers VividRP.PerObjectBuffer.PrepareAndBind and " +
                "VividRP.PerObjectBuffer.Upload to inspect the upload path in a real rendered frame.");
            return builder.ToString();
        }

        private static void AppendCase(
            StringBuilder builder,
            VividPerObjectCpuBenchmarkCase result)
        {
            builder.Append(result.Name.PadRight(40))
                .Append(result.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture).PadLeft(10))
                .Append(result.NanosecondsPerOperation.ToString("F2", CultureInfo.InvariantCulture).PadLeft(11))
                .Append(result.AllocatedBytesPerOperation.ToString("F3", CultureInfo.InvariantCulture).PadLeft(10))
                .Append("   ")
                .AppendLine(result.OperationUnit);
        }

        private static double Divide(double numerator, double denominator)
        {
            return denominator > 0.0 ? numerator / denominator : 0.0;
        }
    }

    public static class VividPerObjectCpuBenchmark
    {
        private const string RendererOperationUnit = "renderer write";
        private const string FrameOperationUnit = "frame submit";

        private static readonly ProfilerMarker s_MaterialPropertyBlockMarker =
            new("VividRP.PerObjectBenchmark.MaterialPropertyBlock");
        private static readonly ProfilerMarker s_PerObjectBufferMarker =
            new("VividRP.PerObjectBenchmark.PerObjectBuffer");
        private static readonly ProfilerMarker s_PerObjectBufferSubmitMarker =
            new("VividRP.PerObjectBenchmark.PerObjectBufferSubmit");

        public static VividPerObjectCpuBenchmarkReport Run(
            IReadOnlyList<Renderer> renderers,
            int warmupIterations = 16,
            int measurementIterations = 128,
            VividPerObjectColorExampleController.PropertyAccessMode perObjectAccessMode =
                VividPerObjectColorExampleController.PropertyAccessMode.CachedHandle)
        {
            Renderer[] targets = ValidateAndCopyRenderers(renderers);
            if (warmupIterations < 0)
                throw new ArgumentOutOfRangeException(nameof(warmupIterations));
            if (measurementIterations <= 0)
                throw new ArgumentOutOfRangeException(nameof(measurementIterations));
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new NotSupportedException(
                    "The CPU benchmark requires a graphics device so pending SSBO ranges can be " +
                    "submitted and cleared between simulated frames.");
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (VividPerObjectBuffer.IsBound(targets[i]))
                {
                    throw new InvalidOperationException(
                        $"Renderer '{targets[i].name}' is already bound to VividPerObjectBuffer. " +
                        "Use dedicated benchmark Renderers so their existing layout is not replaced.");
                }
            }

            PropertyBlockSnapshot[] snapshots = CapturePropertyBlocks(targets);
            Color[] colorsA = CreateColors(targets.Length, alternate: false);
            Color[] colorsB = CreateColors(targets.Length, alternate: true);
            var materialPropertyBlock = new MaterialPropertyBlock();
            var blocks = new VividPerObjectBlock[targets.Length];
            var commandBuffer = new CommandBuffer
            {
                name = "Vivid Per-Object CPU Benchmark Submit",
            };
            int boundCount = 0;

            try
            {
                ClearPropertyBlocks(snapshots);

                WarmupMaterialPropertyBlock(
                    targets,
                    materialPropertyBlock,
                    colorsA,
                    colorsB,
                    warmupIterations,
                    changing: true);
                VividPerObjectCpuBenchmarkCase materialPropertyBlockChanging =
                    MeasureMaterialPropertyBlock(
                        targets,
                        materialPropertyBlock,
                        colorsA,
                        colorsB,
                        measurementIterations,
                        changing: true);

                WarmupMaterialPropertyBlock(
                    targets,
                    materialPropertyBlock,
                    colorsA,
                    colorsB,
                    warmupIterations,
                    changing: false);
                VividPerObjectCpuBenchmarkCase materialPropertyBlockUnchanged =
                    MeasureMaterialPropertyBlock(
                        targets,
                        materialPropertyBlock,
                        colorsA,
                        colorsB,
                        measurementIterations,
                        changing: false);

                ClearPropertyBlocks(snapshots);
                VividPerObjectPropertyHandle colorProperty =
                    VividPerObjectColorExampleLayout.ColorProperty;
                for (int i = 0; i < targets.Length; i++)
                {
                    blocks[i] =
                        VividPerObjectBuffer.Bind<VividPerObjectColorExampleLayout>(targets[i]);
                    boundCount++;
                }

                SubmitPendingChanges(commandBuffer);
                WarmupPerObjectBuffer(
                    blocks,
                    colorsA,
                    colorsB,
                    colorProperty,
                    commandBuffer,
                    warmupIterations,
                    changing: true,
                    perObjectAccessMode);
                PerObjectMeasurement changingMeasurement = MeasurePerObjectBuffer(
                    blocks,
                    colorsA,
                    colorsB,
                    colorProperty,
                    commandBuffer,
                    measurementIterations,
                    changing: true,
                    perObjectAccessMode);

                WarmupPerObjectBuffer(
                    blocks,
                    colorsA,
                    colorsB,
                    colorProperty,
                    commandBuffer,
                    warmupIterations,
                    changing: false,
                    perObjectAccessMode);
                PerObjectMeasurement unchangedMeasurement = MeasurePerObjectBuffer(
                    blocks,
                    colorsA,
                    colorsB,
                    colorProperty,
                    commandBuffer,
                    measurementIterations,
                    changing: false,
                    perObjectAccessMode);

                return new VividPerObjectCpuBenchmarkReport(
                    targets.Length,
                    warmupIterations,
                    measurementIterations,
                    perObjectAccessMode,
                    materialPropertyBlockChanging,
                    changingMeasurement.Write,
                    changingMeasurement.Submit,
                    materialPropertyBlockUnchanged,
                    unchangedMeasurement.Write,
                    unchangedMeasurement.Submit);
            }
            finally
            {
                commandBuffer.Dispose();
                for (int i = 0; i < boundCount; i++)
                {
                    if (blocks[i].IsValid)
                        VividPerObjectBuffer.Unbind(targets[i]);
                }

                RestorePropertyBlocks(snapshots);
            }
        }

        private static Renderer[] ValidateAndCopyRenderers(IReadOnlyList<Renderer> renderers)
        {
            if (renderers == null)
                throw new ArgumentNullException(nameof(renderers));

            var targets = new List<Renderer>(renderers.Count);
            var uniqueRenderers = new HashSet<Renderer>();
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                {
                    throw new NotSupportedException(
                        $"Renderer '{renderer.name}' is a {renderer.GetType().Name}. " +
                        "Only MeshRenderer and SkinnedMeshRenderer are supported.");
                }
                if (uniqueRenderers.Add(renderer))
                    targets.Add(renderer);
            }

            if (targets.Count == 0)
                throw new ArgumentException("At least one supported Renderer is required.", nameof(renderers));
            return targets.ToArray();
        }

        private static PropertyBlockSnapshot[] CapturePropertyBlocks(Renderer[] renderers)
        {
            var snapshots = new PropertyBlockSnapshot[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                MaterialPropertyBlock rendererPropertyBlock = null;
                MaterialPropertyBlock[] materialPropertyBlocks =
                    Array.Empty<MaterialPropertyBlock>();
                if (renderer.HasPropertyBlock())
                {
                    rendererPropertyBlock = CapturePropertyBlock(renderer, materialIndex: -1);
                    int materialCount = renderer.sharedMaterials.Length;
                    materialPropertyBlocks = new MaterialPropertyBlock[materialCount];
                    for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
                    {
                        materialPropertyBlocks[materialIndex] =
                            CapturePropertyBlock(renderer, materialIndex);
                    }
                }

                snapshots[i] =
                    new PropertyBlockSnapshot(
                        renderer,
                        rendererPropertyBlock,
                        materialPropertyBlocks);
            }

            return snapshots;
        }

        private static MaterialPropertyBlock CapturePropertyBlock(
            Renderer renderer,
            int materialIndex)
        {
            var propertyBlock = new MaterialPropertyBlock();
            if (materialIndex < 0)
                renderer.GetPropertyBlock(propertyBlock);
            else
                renderer.GetPropertyBlock(propertyBlock, materialIndex);
            return propertyBlock.isEmpty ? null : propertyBlock;
        }

        private static void RestorePropertyBlocks(PropertyBlockSnapshot[] snapshots)
        {
            ClearPropertyBlocks(snapshots);
            for (int i = 0; i < snapshots.Length; i++)
            {
                PropertyBlockSnapshot snapshot = snapshots[i];
                if (snapshot.Renderer == null)
                    continue;

                if (snapshot.RendererPropertyBlock != null)
                    snapshot.Renderer.SetPropertyBlock(snapshot.RendererPropertyBlock);
                for (int materialIndex = 0;
                    materialIndex < snapshot.MaterialPropertyBlocks.Length;
                    materialIndex++)
                {
                    MaterialPropertyBlock propertyBlock =
                        snapshot.MaterialPropertyBlocks[materialIndex];
                    if (propertyBlock != null)
                        snapshot.Renderer.SetPropertyBlock(propertyBlock, materialIndex);
                }
            }
        }

        private static void ClearPropertyBlocks(PropertyBlockSnapshot[] snapshots)
        {
            for (int i = 0; i < snapshots.Length; i++)
            {
                PropertyBlockSnapshot snapshot = snapshots[i];
                if (snapshot.Renderer == null)
                    continue;

                snapshot.Renderer.SetPropertyBlock(null);
                for (int materialIndex = 0;
                    materialIndex < snapshot.MaterialPropertyBlocks.Length;
                    materialIndex++)
                {
                    snapshot.Renderer.SetPropertyBlock(null, materialIndex);
                }
            }
        }

        private static Color[] CreateColors(int count, bool alternate)
        {
            var colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                float value = (i % 251) / 250.0f;
                colors[i] = alternate
                    ? new Color(1.0f - value, 0.25f, value, 1.0f)
                    : new Color(value, 1.0f - value, 0.5f, 1.0f);
            }
            return colors;
        }

        private static void WarmupMaterialPropertyBlock(
            Renderer[] renderers,
            MaterialPropertyBlock propertyBlock,
            Color[] colorsA,
            Color[] colorsB,
            int iterations,
            bool changing)
        {
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                Color[] colors = changing && (iteration & 1) != 0 ? colorsB : colorsA;
                PushMaterialPropertyBlock(renderers, propertyBlock, colors);
            }
        }

        private static VividPerObjectCpuBenchmarkCase MeasureMaterialPropertyBlock(
            Renderer[] renderers,
            MaterialPropertyBlock propertyBlock,
            Color[] colorsA,
            Color[] colorsB,
            int iterations,
            bool changing)
        {
            long elapsedTicks = 0;
            long allocatedBytes = 0;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                Color[] colors = changing && (iteration & 1) != 0 ? colorsB : colorsA;
                using (s_MaterialPropertyBlockMarker.Auto())
                {
                    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    PushMaterialPropertyBlock(renderers, propertyBlock, colors);
                    elapsedTicks += Stopwatch.GetTimestamp() - start;
                    allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                }
            }

            string changeLabel = changing ? "changing" : "unchanged";
            return new VividPerObjectCpuBenchmarkCase(
                $"MaterialPropertyBlock / {changeLabel}",
                RendererOperationUnit,
                (long)renderers.Length * iterations,
                elapsedTicks,
                allocatedBytes);
        }

        private static void PushMaterialPropertyBlock(
            Renderer[] renderers,
            MaterialPropertyBlock propertyBlock,
            Color[] colors)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                propertyBlock.SetColor(
                    VividPerObjectColorExampleLayout.ColorPropertyId,
                    colors[i]);
                renderers[i].SetPropertyBlock(propertyBlock);
            }
        }

        private static void WarmupPerObjectBuffer(
            VividPerObjectBlock[] blocks,
            Color[] colorsA,
            Color[] colorsB,
            VividPerObjectPropertyHandle colorProperty,
            CommandBuffer commandBuffer,
            int iterations,
            bool changing,
            VividPerObjectColorExampleController.PropertyAccessMode accessMode)
        {
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                Color[] colors = changing && (iteration & 1) != 0 ? colorsB : colorsA;
                PushPerObjectBuffer(blocks, colors, colorProperty, accessMode);
                SubmitPendingChanges(commandBuffer);
            }
        }

        private static PerObjectMeasurement MeasurePerObjectBuffer(
            VividPerObjectBlock[] blocks,
            Color[] colorsA,
            Color[] colorsB,
            VividPerObjectPropertyHandle colorProperty,
            CommandBuffer commandBuffer,
            int iterations,
            bool changing,
            VividPerObjectColorExampleController.PropertyAccessMode accessMode)
        {
            long writeTicks = 0;
            long writeAllocatedBytes = 0;
            long submitTicks = 0;
            long submitAllocatedBytes = 0;

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                Color[] colors = changing && (iteration & 1) != 0 ? colorsB : colorsA;
                using (s_PerObjectBufferMarker.Auto())
                {
                    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    PushPerObjectBuffer(blocks, colors, colorProperty, accessMode);
                    writeTicks += Stopwatch.GetTimestamp() - start;
                    writeAllocatedBytes +=
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                }

                using (s_PerObjectBufferSubmitMarker.Auto())
                {
                    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    SubmitPendingChanges(commandBuffer);
                    submitTicks += Stopwatch.GetTimestamp() - start;
                    submitAllocatedBytes +=
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                }
            }

            string changeLabel = changing ? "changing" : "unchanged";
            return new PerObjectMeasurement(
                new VividPerObjectCpuBenchmarkCase(
                    $"PerObjectBuffer / {changeLabel}",
                    RendererOperationUnit,
                    (long)blocks.Length * iterations,
                    writeTicks,
                    writeAllocatedBytes),
                new VividPerObjectCpuBenchmarkCase(
                    $"PerObjectBuffer submit / {changeLabel}",
                    FrameOperationUnit,
                    iterations,
                    submitTicks,
                    submitAllocatedBytes));
        }

        private static void PushPerObjectBuffer(
            VividPerObjectBlock[] blocks,
            Color[] colors,
            VividPerObjectPropertyHandle colorProperty,
            VividPerObjectColorExampleController.PropertyAccessMode accessMode)
        {
            switch (accessMode)
            {
                case VividPerObjectColorExampleController.PropertyAccessMode.CachedHandle:
                    for (int i = 0; i < blocks.Length; i++)
                        blocks[i].SetColor(colorProperty, colors[i]);
                    break;
                case VividPerObjectColorExampleController.PropertyAccessMode.PropertyId:
                    for (int i = 0; i < blocks.Length; i++)
                    {
                        blocks[i].SetColor(
                            VividPerObjectColorExampleLayout.ColorPropertyId,
                            colors[i]);
                    }
                    break;
                case VividPerObjectColorExampleController.PropertyAccessMode.PropertyName:
                    for (int i = 0; i < blocks.Length; i++)
                    {
                        blocks[i].SetColor(
                            VividPerObjectColorExampleLayout.ColorPropertyName,
                            colors[i]);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(accessMode));
            }
        }

        private static void SubmitPendingChanges(CommandBuffer commandBuffer)
        {
            commandBuffer.Clear();
            VividPerObjectBuffer.PrepareAndBind(commandBuffer);
            Graphics.ExecuteCommandBuffer(commandBuffer);
            commandBuffer.Clear();
        }

        private readonly struct PropertyBlockSnapshot
        {
            internal PropertyBlockSnapshot(
                Renderer renderer,
                MaterialPropertyBlock rendererPropertyBlock,
                MaterialPropertyBlock[] materialPropertyBlocks)
            {
                Renderer = renderer;
                RendererPropertyBlock = rendererPropertyBlock;
                MaterialPropertyBlocks = materialPropertyBlocks;
            }

            internal Renderer Renderer { get; }

            internal MaterialPropertyBlock RendererPropertyBlock { get; }

            internal MaterialPropertyBlock[] MaterialPropertyBlocks { get; }
        }

        private readonly struct PerObjectMeasurement
        {
            internal PerObjectMeasurement(
                VividPerObjectCpuBenchmarkCase write,
                VividPerObjectCpuBenchmarkCase submit)
            {
                Write = write;
                Submit = submit;
            }

            internal VividPerObjectCpuBenchmarkCase Write { get; }

            internal VividPerObjectCpuBenchmarkCase Submit { get; }
        }
    }

}
