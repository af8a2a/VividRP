namespace UnityEngine.Rendering.Universal
{
    // We use a different structure for directional light because these is a lot of data there
    // and it will add too much useless stuff for other lights
    // Note: In order to support HLSL array generation, we need to use fixed arrays and so a unsafe context for this struct
    unsafe struct VividDirectionalShadowData
    {
        // We can't use Vector4 here because the vector4[] makes this struct non blittable

        [HLSLArray(4, typeof(Vector4))] public fixed float sphereCascades[4 * 4];

        [SurfaceDataAttributes(precision = FieldPrecision.Real)]
        public Vector4 cascadeDirection;

        [HLSLArray(4, typeof(float))] [SurfaceDataAttributes(precision = FieldPrecision.Real)]
        public fixed float cascadeBorders[4];

        public float fadeScale;
        public float fadeBias;
    };


    [GenerateHLSL(needAccessors = false)]
    struct VividShadowData
    {
        public Vector3 rot0;
        public Vector3 rot1;
        public Vector3 rot2;
        public Vector3 pos;
        public Vector4 proj;

        public Vector2 atlasOffset;
        public float worldTexelSize;
        public float normalBias;

        [SurfaceDataAttributes(precision = FieldPrecision.Real)]
        public Vector4 zBufferParam;

        public Vector4 shadowMapSize;

        public Vector4 shadowFilterParams0;
        public Vector4 dirLightPCSSParams0;
        public Vector4 dirLightPCSSParams1;

        public Vector3 cacheTranslationDelta;
        public float isInCachedAtlas;

        public Matrix4x4 shadowToWorld;
    }
    
    
    
    struct VividShadowCullingSplit
    {
        public Matrix4x4 view;
        public Matrix4x4 deviceProjectionMatrix;
        public Matrix4x4 deviceProjectionYFlip; // Use the y flipped device projection matrix as light projection matrix
        public Matrix4x4 projection;
        public Matrix4x4 invViewProjection;
        public Vector4 deviceProjection;
        public Vector4 cullingSphere;
        public Vector2 viewportSize;
        public float forwardOffset;
        public int splitIndex;
    }

}