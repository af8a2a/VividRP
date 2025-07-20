using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public partial class ExposurePass : ScriptableRenderPass, IDisposable
    {
        public class ShaderIDs
        {
            public static readonly int _ExposureTexture = Shader.PropertyToID("_ExposureTexture");

            public static readonly int _PrevExposureTexture = Shader.PropertyToID("_PrevExposureTexture");

            // Note that this is a separate name because is bound locally to a exposure shader, while _PrevExposureTexture is bound globally for everything else.
            public static readonly int _PreviousExposureTexture = Shader.PropertyToID("_PreviousExposureTexture");
            public static readonly int _ExposureDebugTexture = Shader.PropertyToID("_ExposureDebugTexture");
            public static readonly int _ExposureParams = Shader.PropertyToID("_ExposureParams");
            public static readonly int _ExposureParams2 = Shader.PropertyToID("_ExposureParams2");
            public static readonly int _ExposureDebugParams = Shader.PropertyToID("_ExposureDebugParams");
            public static readonly int _HistogramExposureParams = Shader.PropertyToID("_HistogramExposureParams");
            public static readonly int _HistogramBuffer = Shader.PropertyToID("_HistogramBuffer");
            public static readonly int _FullImageHistogram = Shader.PropertyToID("_FullImageHistogram");
            public static readonly int _xyBuffer = Shader.PropertyToID("_xyBuffer");
            public static readonly int _HDRxyBufferDebugParams = Shader.PropertyToID("_HDRxyBufferDebugParams");
            public static readonly int _HDRDebugParams = Shader.PropertyToID("_HDRDebugParams");
            public static readonly int _AdaptationParams = Shader.PropertyToID("_AdaptationParams");
            public static readonly int _ExposureCurveTexture = Shader.PropertyToID("_ExposureCurveTexture");
            public static readonly int _ExposureWeightMask = Shader.PropertyToID("_ExposureWeightMask");
            public static readonly int _ProceduralMaskParams = Shader.PropertyToID("_ProceduralMaskParams");
            public static readonly int _ProceduralMaskParams2 = Shader.PropertyToID("_ProceduralMaskParams2");
            public static readonly int _Variants = Shader.PropertyToID("_Variants");
            public static readonly int _InputTexture = Shader.PropertyToID("_InputTexture");
            public static readonly int _InputTexture2 = Shader.PropertyToID("_InputTexture2");
            public static readonly int _InputTextureArray = Shader.PropertyToID("_InputTextureArray");
            public static readonly int _InputTextureMSAA = Shader.PropertyToID("_InputTextureMSAA");
            public static readonly int _OutputTexture = Shader.PropertyToID("_OutputTexture");
            public static readonly int _SourceTexture = Shader.PropertyToID("_SourceTexture");
            public static readonly int _InputHistoryTexture = Shader.PropertyToID("_InputHistoryTexture");
            public static readonly int _OutputHistoryTexture = Shader.PropertyToID("_OutputHistoryTexture");
            public static readonly int _InputVelocityMagnitudeHistory = Shader.PropertyToID("_InputVelocityMagnitudeHistory");
            public static readonly int _OutputVelocityMagnitudeHistory = Shader.PropertyToID("_OutputVelocityMagnitudeHistory");
            public static readonly int _OutputDepthTexture = Shader.PropertyToID("_OutputDepthTexture");
            public static readonly int _OutputMotionVectorTexture = Shader.PropertyToID("_OutputMotionVectorTexture");
            public static readonly int _OutputResolution = Shader.PropertyToID("_OutputResolution");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsExposureFixed() => m_Exposure.mode.value == ExposureMode.Fixed;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            m_Exposure = VolumeManager.instance.stack.GetComponent<ExposureSetting>();

            if (IsExposureFixed())
            {
                DoFixedExposure(renderGraph,frameData);
            }
            else
            {
                
                DynamicExposurePass(renderGraph, frameData);
            }
        }

        public void Dispose()
        {
            m_ExposureCurveTextureRT?.Release();
        }
    }
}