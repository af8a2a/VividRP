using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core.Sigma
{
    internal readonly struct SigmaNativePluginInput
    {
        public SigmaNativePluginInput(
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            Matrix4x4 worldToViewPrev,
            Matrix4x4 viewToClipPrev,
            Vector3 lightDirectionWS,
            int width,
            int height,
            int widthPrev,
            int heightPrev,
            uint frameIndex,
            float denoisingRange,
            float planeDistanceSensitivity,
            uint maxStabilizedFrameNum,
            bool isOrthographic,
            bool hasValidHistory)
        {
            WorldToView = worldToView;
            ViewToClip = viewToClip;
            WorldToViewPrev = worldToViewPrev;
            ViewToClipPrev = viewToClipPrev;
            LightDirectionWS = lightDirectionWS;
            Width = width;
            Height = height;
            WidthPrev = widthPrev;
            HeightPrev = heightPrev;
            FrameIndex = frameIndex;
            DenoisingRange = denoisingRange;
            PlaneDistanceSensitivity = planeDistanceSensitivity;
            MaxStabilizedFrameNum = maxStabilizedFrameNum;
            IsOrthographic = isOrthographic;
            HasValidHistory = hasValidHistory;
        }

        public Matrix4x4 WorldToView { get; }

        public Matrix4x4 ViewToClip { get; }

        public Matrix4x4 WorldToViewPrev { get; }

        public Matrix4x4 ViewToClipPrev { get; }

        public Vector3 LightDirectionWS { get; }

        public int Width { get; }

        public int Height { get; }

        public int WidthPrev { get; }

        public int HeightPrev { get; }

        public uint FrameIndex { get; }

        public float DenoisingRange { get; }

        public float PlaneDistanceSensitivity { get; }

        public uint MaxStabilizedFrameNum { get; }

        public bool IsOrthographic { get; }

        public bool HasValidHistory { get; }
    }

    internal sealed class SigmaNativePluginSession : IDisposable
    {
        private IntPtr m_Context;

        private SigmaNativePluginSession(IntPtr context)
        {
            m_Context = context;
        }

        public static SigmaNativePluginSession TryCreate()
        {
            IntPtr context = SigmaNativePluginBindings.TryCreateContext();
            return context != IntPtr.Zero ? new SigmaNativePluginSession(context) : null;
        }

        public bool TryComputeSharedConstants(
            in SigmaNativePluginInput input,
            out SigmaSharedConstants constants)
        {
            constants = default;

            if (m_Context == IntPtr.Zero)
            {
                return false;
            }

            return SigmaNativePluginBindings.TryComputeSharedConstants(m_Context, input, out constants);
        }

        public void Dispose()
        {
            if (m_Context == IntPtr.Zero)
            {
                return;
            }

            SigmaNativePluginBindings.ReleaseContext(m_Context);
            m_Context = IntPtr.Zero;
        }
    }

    internal static class SigmaNativePluginBindings
    {
        private const string DLLName =
#if (PLATFORM_IOS || PLATFORM_TVOS || PLATFORM_BRATWURST || PLATFORM_SWITCH) && !UNITY_EDITOR
            "__Internal";
#else
            "NRDUnityPlugin";
#endif

        private static bool s_HasCheckedAvailability;
        private static bool s_IsAvailable;

        [DllImport(DLLName)]
        private static extern int NRD_Test();

        [DllImport(DLLName)]
        private static extern IntPtr NRD_GetContext();

        [DllImport(DLLName)]
        private static extern void NRD_ReleaseContext(IntPtr context);

        [DllImport(DLLName)]
        private static extern NrdResult NRD_SetCommonSettings(
            IntPtr context,
            ref NrdCommonSettings commonSettings);

        [DllImport(DLLName)]
        private static extern void NRD_SetupSigmaConstBuffer(
            IntPtr context,
            ref NrdCommonSettings commonSettings,
            ref NrdSigmaSettings sigmaSettings,
            out SigmaSharedConstants data);

        public static IntPtr TryCreateContext()
        {
            EnsureAvailability();
            if (!s_IsAvailable)
            {
                return IntPtr.Zero;
            }

            try
            {
                return NRD_GetContext();
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                             || exception is EntryPointNotFoundException
                                             || exception is BadImageFormatException)
            {
                s_IsAvailable = false;
                return IntPtr.Zero;
            }
        }

        public static void ReleaseContext(IntPtr context)
        {
            if (context == IntPtr.Zero)
            {
                return;
            }

            try
            {
                NRD_ReleaseContext(context);
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                             || exception is EntryPointNotFoundException
                                             || exception is BadImageFormatException)
            {
                s_IsAvailable = false;
            }
        }

        public static bool TryComputeSharedConstants(
            IntPtr context,
            in SigmaNativePluginInput input,
            out SigmaSharedConstants constants)
        {
            constants = default;

            if (context == IntPtr.Zero)
            {
                return false;
            }

            var commonSettings = NrdCommonSettings.Create(input);
            var sigmaSettings = NrdSigmaSettings.Create(input);

            try
            {
                NrdResult result = NRD_SetCommonSettings(context, ref commonSettings);
                if (result != NrdResult.SUCCESS)
                {
                    return false;
                }

                NRD_SetupSigmaConstBuffer(context, ref commonSettings, ref sigmaSettings, out constants);
                return true;
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                             || exception is EntryPointNotFoundException
                                             || exception is BadImageFormatException)
            {
                s_IsAvailable = false;
                return false;
            }
        }

        private static void EnsureAvailability()
        {
            if (s_HasCheckedAvailability)
            {
                return;
            }

            s_HasCheckedAvailability = true;

            try
            {
                s_IsAvailable = NRD_Test() > 0;
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                             || exception is EntryPointNotFoundException
                                             || exception is BadImageFormatException)
            {
                s_IsAvailable = false;
            }
        }
    }

    internal enum NrdResult : uint
    {
        SUCCESS,
        FAILURE,
        INVALID_ARGUMENT,
        UNSUPPORTED,
        NON_UNIQUE_IDENTIFIER,

        MAX_NUM
    }

    internal enum NrdAccumulationMode : uint
    {
        CONTINUE,
        RESTART,
        CLEAR_AND_RESTART,

        MAX_NUM
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct NrdCommonSettings
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] viewToClipMatrix;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] viewToClipMatrixPrev;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] worldToViewMatrix;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] worldToViewMatrixPrev;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] worldPrevToWorldMatrix;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] motionVectorScale;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] cameraJitter;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] cameraJitterPrev;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] resourceSize;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] resourceSizePrev;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] rectSize;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] rectSizePrev;

        public float viewZScale;
        public float timeDeltaBetweenFrames;
        public float denoisingRange;
        public float disocclusionThreshold;
        public float disocclusionThresholdAlternate;
        public float cameraAttachedReflectionMaterialID;
        public float strandMaterialID;
        public float historyFixAlternatePixelStrideMaterialID;
        public float strandThickness;
        public float splitScreen;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] printfAt;

        public float debug;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public uint[] rectOrigin;

        public uint frameIndex;
        public NrdAccumulationMode accumulationMode;

        [MarshalAs(UnmanagedType.I1)]
        public bool isMotionVectorInWorldSpace;

        [MarshalAs(UnmanagedType.I1)]
        public bool isHistoryConfidenceAvailable;

        [MarshalAs(UnmanagedType.I1)]
        public bool isDisocclusionThresholdMixAvailable;

        [MarshalAs(UnmanagedType.I1)]
        public bool isBaseColorMetalnessAvailable;

        [MarshalAs(UnmanagedType.I1)]
        public bool enableValidation;

        public static NrdCommonSettings Create(in SigmaNativePluginInput input)
        {
            return new NrdCommonSettings
            {
                viewToClipMatrix = PackMatrix(input.ViewToClip),
                viewToClipMatrixPrev = PackMatrix(input.ViewToClipPrev),
                worldToViewMatrix = PackMatrix(input.WorldToView),
                worldToViewMatrixPrev = PackMatrix(input.WorldToViewPrev),
                worldPrevToWorldMatrix = new float[16]
                {
                    1f, 0f, 0f, 0f,
                    0f, 1f, 0f, 0f,
                    0f, 0f, 1f, 0f,
                    0f, 0f, 0f, 1f
                },
                motionVectorScale = new[] { 1.0f, 1.0f, 0.0f },
                cameraJitter = new float[2],
                cameraJitterPrev = new float[2],
                resourceSize = new[] { ToUInt16(input.Width), ToUInt16(input.Height) },
                resourceSizePrev = new[] { ToUInt16(input.WidthPrev), ToUInt16(input.HeightPrev) },
                rectSize = new[] { ToUInt16(input.Width), ToUInt16(input.Height) },
                rectSizePrev = new[] { ToUInt16(input.WidthPrev), ToUInt16(input.HeightPrev) },
                viewZScale = 1.0f,
                timeDeltaBetweenFrames = 0.0f,
                denoisingRange = Mathf.Max(0.0f, input.DenoisingRange),
                disocclusionThreshold = 0.01f,
                disocclusionThresholdAlternate = 0.05f,
                cameraAttachedReflectionMaterialID = 999.0f,
                strandMaterialID = 999.0f,
                historyFixAlternatePixelStrideMaterialID = 999.0f,
                strandThickness = 80e-6f,
                splitScreen = 0.0f,
                printfAt = new ushort[2],
                debug = 0.0f,
                rectOrigin = new uint[2],
                frameIndex = input.FrameIndex,
                accumulationMode = input.HasValidHistory
                    ? NrdAccumulationMode.CONTINUE
                    : NrdAccumulationMode.CLEAR_AND_RESTART,
                isMotionVectorInWorldSpace = false,
                isHistoryConfidenceAvailable = false,
                isDisocclusionThresholdMixAvailable = false,
                isBaseColorMetalnessAvailable = false,
                enableValidation = false
            };
        }

        private static ushort ToUInt16(int value)
        {
            return (ushort)Mathf.Clamp(value, 1, ushort.MaxValue);
        }

        private static float[] PackMatrix(Matrix4x4 matrix)
        {
            return new[]
            {
                matrix.m00, matrix.m10, matrix.m20, matrix.m30,
                matrix.m01, matrix.m11, matrix.m21, matrix.m31,
                matrix.m02, matrix.m12, matrix.m22, matrix.m32,
                matrix.m03, matrix.m13, matrix.m23, matrix.m33
            };
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct NrdSigmaSettings
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] lightDirection;

        public float planeDistanceSensitivity;
        public uint maxStabilizedFrameNum;

        public static NrdSigmaSettings Create(in SigmaNativePluginInput input)
        {
            Vector3 lightDirection = input.LightDirectionWS.sqrMagnitude > 0.0f
                ? input.LightDirectionWS.normalized
                : Vector3.down;

            return new NrdSigmaSettings
            {
                lightDirection = new[] { lightDirection.x, lightDirection.y, lightDirection.z },
                planeDistanceSensitivity = Mathf.Clamp01(input.PlaneDistanceSensitivity),
                maxStabilizedFrameNum = input.MaxStabilizedFrameNum
            };
        }
    }
}
