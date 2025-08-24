using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class PaniniProjectionPass
    {
        #region Panini

        Material material;

        private class PaniniProjectionPassData
        {
            internal TextureHandle destinationTexture;
            internal TextureHandle sourceTexture;
            internal Material material;
            internal Vector4 paniniParams;
            internal bool isPaniniGeneric;
        }

        static Vector2 CalcViewExtents(float actualWidth, float actualHeight, float fieldOfView)
        {
            float fovY = fieldOfView * Mathf.Deg2Rad;
            float aspect = actualWidth / actualHeight;

            float viewExtY = Mathf.Tan(0.5f * fovY);
            float viewExtX = aspect * viewExtY;

            return new Vector2(viewExtX, viewExtY);
        }

        static Vector2 CalcCropExtents(float actualWidth, float actualHeight, float fieldOfView, float d)
        {
            // given
            //    S----------- E--X-------
            //    |    `  ~.  /,´
            //    |-- ---    Q
            //    |        ,/    `
            //  1 |      ,´/       `
            //    |    ,´ /         ´
            //    |  ,´  /           ´
            //    |,`   /             ,
            //    O    /
            //    |   /               ,
            //  d |  /
            //    | /                ,
            //    |/                .
            //    P
            //    |              ´
            //    |         , ´
            //    +-    ´
            //
            // have X
            // want to find E

            float viewDist = 1.0f + d;

            Vector2 projPos = CalcViewExtents(actualWidth, actualHeight, fieldOfView);
            float projHyp = Mathf.Sqrt(projPos.x * projPos.x + 1.0f);

            float cylDistMinusD = 1.0f / projHyp;
            float cylDist = cylDistMinusD + d;
            Vector2 cylPos = projPos * cylDistMinusD;

            return cylPos * (viewDist / cylDist);
        }

        static Vector2 Panini_Generic_Inv(Vector2 projPos, float d)
        {
            // given
            //    S----------- E--X-------
            //    |    `  ~.  /,´
            //    |-- ---    Q
            //    |        ,/    `
            //  1 |      ,´/       `
            //    |    ,´ /         ´
            //    |  ,´  /           ´
            //    |,`   /             ,
            //    O    /
            //    |   /               ,
            //  d |  /
            //    | /                ,
            //    |/                .
            //    P
            //    |              ´
            //    |         , ´
            //    +-    ´
            //
            // have X
            // want to find E

            float viewDist = 1.0f + d;
            float projHyp = Mathf.Sqrt(projPos.x * projPos.x + 1.0f);

            float cylDistMinusD = 1.0f / projHyp;
            float cylDist = cylDistMinusD + d;
            Vector2 cylPos = projPos * cylDistMinusD;

            return cylPos * (viewDist / cylDist);
        }


        Vector2 CalcCropExtents(UniversalCameraData cameraData, float d)
        {
            // given
            //    S----------- E--X-------
            //    |    `  ~.  /,´
            //    |-- ---    Q
            //    |        ,/    `
            //  1 |      ,´/       `
            //    |    ,´ /         ´
            //    |  ,´  /           ´
            //    |,`   /             ,
            //    O    /
            //    |   /               ,
            //  d |  /
            //    | /                ,
            //    |/                .
            //    P
            //    |              ´
            //    |         , ´
            //    +-    ´
            //
            // have X
            // want to find E

            float viewDist = 1f + d;

            var projPos = CalcViewExtents(cameraData);
            var projHyp = Mathf.Sqrt(projPos.x * projPos.x + 1f);

            float cylDistMinusD = 1f / projHyp;
            float cylDist = cylDistMinusD + d;
            var cylPos = projPos * cylDistMinusD;

            return cylPos * (viewDist / cylDist);
        }

        Vector2 CalcViewExtents(UniversalCameraData cameraData)
        {
            float fovY = cameraData.camera.fieldOfView * Mathf.Deg2Rad;
            float aspect = cameraData.aspectRatio;

            float viewExtY = Mathf.Tan(0.5f * fovY);
            float viewExtX = aspect * viewExtY;

            return new Vector2(viewExtX, viewExtY);
        }

        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            if (!material)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<PostProcessingRuntimeShader>();
                material = CoreUtils.CreateEngineMaterial(runtimeShader.paniniProjection);
            }


            var cameraData = frameData.Get<UniversalCameraData>();

            var paniniProjection = VolumeManager.instance.stack.GetComponent<PaniniProjection>();


            bool usePaniniProjection = paniniProjection.IsActive() && !cameraData.isSceneViewCamera;

            if (!usePaniniProjection)
            {
                return source;
            }

            var destination = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                enableRandomWrite = true,
                format = renderGraph.GetTextureDesc(source).format,
                name = "_PaniniProjectionTarget",
                clearBuffer = true,
                filterMode = FilterMode.Bilinear,
            });

            float distance = paniniProjection.distance.value;
            var viewExtents = CalcViewExtents(cameraData);
            var cropExtents = CalcCropExtents(cameraData, distance);

            float scaleX = cropExtents.x / viewExtents.x;
            float scaleY = cropExtents.y / viewExtents.y;
            float scaleF = Mathf.Min(scaleX, scaleY);

            float paniniD = distance;
            float paniniS = Mathf.Lerp(1f, Mathf.Clamp01(scaleF), paniniProjection.cropToFit.value);

            using (var builder = renderGraph.AddRasterRenderPass<PaniniProjectionPassData>("Panini Projection", out var passData,
                       ProfilingSampler.Get(URPProfileId.PaniniProjection)))
            {
                builder.AllowGlobalStateModification(true);
                passData.destinationTexture = destination;
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);
                passData.material = material;
                passData.paniniParams = new Vector4(viewExtents.x, viewExtents.y, paniniD, paniniS);
                passData.isPaniniGeneric = 1f - Mathf.Abs(paniniD) > float.Epsilon;

                builder.SetRenderFunc(static (PaniniProjectionPassData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    RTHandle sourceTextureHdl = data.sourceTexture;

                    cmd.SetGlobalVector(ShaderConstants._Params, data.paniniParams);
                    data.material.EnableKeyword(data.isPaniniGeneric ? ShaderKeywordStrings.PaniniGeneric : ShaderKeywordStrings.PaniniUnitDistance);

                    Vector2 viewportScale = sourceTextureHdl.useScaling
                        ? new Vector2(sourceTextureHdl.rtHandleProperties.rtHandleScale.x, sourceTextureHdl.rtHandleProperties.rtHandleScale.y)
                        : Vector2.one;
                    Blitter.BlitTexture(cmd, sourceTextureHdl, viewportScale, data.material, 0);
                });

                return destination;
            }
        }

        #endregion
    }
}