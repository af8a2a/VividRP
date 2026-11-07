// Copied into an isolated project's Assets/Editor by Validate-Hlsl2021.py.
// This is a direct Editor/GPU smoke probe, not a Unity Test Framework test.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class Hlsl2021UnityProbe
{
    private static readonly string[] Keywords = {
        "_FILM_GRAIN", "_VIGNETTE", "_BLOOM", "_BLOOM_HQ", "_BLOOM_DIRT", "HDR_COLORSPACE_CONVERSION"
    };

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12 &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan)
                throw new Exception("Run with -force-d3d12 or -force-vulkan, without -nographics.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string languageFailure = null;
            foreach (bool templates in new[] { false, true })
            {
                try { ValidateLanguage(templates); }
                catch (Exception error)
                {
                    languageFailure += $"{(templates ? "Template/operator" : "Short-circuit")} probe: {error.Message}\n";
                    Debug.LogError("HLSL 2021 language probe failed; continuing independent checks: " + error);
                }
            }
            if (Environment.GetCommandLineArgs().Contains("-vividHlslLanguageOnly"))
            {
                File.WriteAllText("Hlsl2021LanguageResult.txt", languageFailure ?? "All language probes passed.");
                EditorApplication.Exit(languageFailure == null ? 0 : 2);
                return;
            }
            // Select the real shader's RenderPipeline tag without loading VividRP C#.
            Shader.globalRenderPipeline = "VividRenderPipeline";
            var current = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/UberPost.shader"));
            var baseline = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/UberPost2018.shader"));
            var texture = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true);
            var pixels = new Color[256];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color((i % 16 + 1) / 4f, (i / 16 + 1) / 8f, (i % 7 + 1) / 3f, (i % 5 + 1) / 5f);
            texture.SetPixels(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            var lut = new Texture3D(8, 8, 8, TextureFormat.RGBAFloat, false);
            var lutPixels = new Color[512];
            for (int z = 0; z < 8; z++)
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                lutPixels[x + y * 8 + z * 64] = new Color(x / 7f, y / 7f, z / 7f, 1);
            lut.SetPixels(lutPixels);
            lut.Apply();
            var exposure = new ComputeBuffer(1, 16);
            var preExposure = new ComputeBuffer(1, 16);
            exposure.SetData(new[] { new Vector4(1.5f, 0, 0, 0) });
            preExposure.SetData(new[] { new Vector4(0.75f, 0, 0, 0) });
            var target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            var readback = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true);
            float maxDifference = 0;
            int comparisons = 0;
            try
            {
                foreach (var material in new[] { current, baseline })
                {
                    material.SetTexture("_BlitTexture", texture);
                    material.SetTexture("_VividColorGradingLut", lut);
                    material.SetTexture("_VividBloomTexture", texture);
                    material.SetTexture("_VividBloomDirtTexture", texture);
                    material.SetTexture("_VividVignetteMask", texture);
                    material.SetTexture("_VividFilmGrainTexture", texture);
                    material.SetBuffer("_VividAutoExposureBuffer", exposure);
                    material.SetBuffer("_VividAutoExposurePreExposureBuffer", preExposure);
                    material.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                    material.SetVector("_VividBloomParams", new Vector4(.1f, .05f, 1, 1));
                    material.SetVector("_VividBloomTint", new Vector4(.8f, .9f, 1, 0));
                    material.SetVector("_VividBloomDirtScale", new Vector4(1, 1, 0, 0));
                    material.SetVector("_VividFilmGrainParams", new Vector4(.1f, .5f, 0, 0));
                    material.SetVector("_VividFilmGrainTexParams", new Vector4(1, 1, .1f, .2f));
                    material.SetVector("_VividVignetteParams2", new Vector4(.5f, 2, 1, 1));
                    material.SetVector("_VividVignetteColor", new Vector4(.1f, .2f, .3f, .6f));
                    material.SetVector("_VividVignetteScreenParams", new Vector4(16, 16, 0, 0));
                    material.SetVector("_HDROutputParams", new Vector4(.005f, 1000, 200, .005f));
                    material.SetVector("_HDROutputParams2", new Vector4(1, .1f, 0, 0));
                }
                Shader.SetGlobalVector("_VividScreenParams", new Vector4(16, 16, 1.0625f, 1.0625f));
                Shader.SetGlobalVector("_VividGlobalMipBias", new Vector4(0, 1, 0, 0));
                for (int mask = 0; mask < 64; mask++)
                for (int mode = 0; mode < 2; mode++)
                {
                    foreach (var material in new[] { current, baseline })
                    {
                        material.shaderKeywords = Keywords.Where((_, bit) => (mask & (1 << bit)) != 0).ToArray();
                        material.SetVector("_VividColorGradingParams", new Vector4(7f / 8f, .5f / 8f, mode, 1.25f));
                        material.SetVector("_VividAutoExposureParams", new Vector4(mode, 0, 0, 0));
                        material.SetVector("_VividVignetteParams1", new Vector4(.4f, .6f, mode, 0));
                        ShaderUtil.CompilePass(material, 0, true);
                        CheckShader(material.shader);
                    }
                    Color[] a = Render(current, target, readback);
                    Color[] b = Render(baseline, target, readback);
                    for (int i = 0; i < a.Length; i++)
                    for (int channel = 0; channel < 4; channel++)
                    {
                        if (float.IsNaN(a[i][channel]) || float.IsInfinity(a[i][channel]) ||
                            float.IsNaN(b[i][channel]) || float.IsInfinity(b[i][channel]))
                            throw new Exception($"Non-finite output: mask={mask}, mode={mode}");
                        maxDifference = Mathf.Max(maxDifference, Mathf.Abs(a[i][channel] - b[i][channel]));
                    }
                    // An analytic no-effect sample detects missing draws or error shaders.
                    if (mask == 0 && mode == 0 && Mathf.Abs(a[0].r - pixels[0].r * 1.25f) > 1e-5f)
                        throw new Exception($"Unexpected no-effect output: {a[0]}");
                    comparisons++;
                }
                if (maxDifference > 1e-5f)
                    throw new Exception($"HLSL 2018/2021 output difference: {maxDifference}");
                string report = $"Unity={Application.unityVersion}\nAPI={SystemInfo.graphicsDeviceType}\nGPU={SystemInfo.graphicsDeviceName}\n" +
                    $"Template/operator/short-circuit probe={(languageFailure == null ? "passed" : "FAILED: " + languageFailure)}\n" +
                    $"UberPost variants=64\nUniform cases=2\nImage comparisons={comparisons}\nMax absolute difference={maxDifference:R}\n";
                File.WriteAllText("Hlsl2021UnityResult.txt", report);
                Debug.Log(report);
            }
            finally
            {
                RenderTexture.active = null;
                exposure.Dispose(); preExposure.Dispose();
                target.Release();
                foreach (var resource in new UnityEngine.Object[] { current, baseline, texture, lut, target, readback })
                    UnityEngine.Object.DestroyImmediate(resource);
            }
            EditorApplication.Exit(languageFailure == null ? 0 : 2);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("Hlsl2021UnityFailure.txt", error.ToString());
            EditorApplication.Exit(1);
        }
    }

    private static void ValidateLanguage(bool templates)
    {
        var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/LanguageProbe.shader"));
        var target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true);
        try
        {
            target.Create();
            if (templates) material.EnableKeyword("PROBE_TEMPLATES");
            ShaderUtil.CompilePass(material, 0, true);
            CheckShader(material.shader);
            foreach (Color output in Render(material, target, readback))
                if (Mathf.Abs(output.r - .42f) > 1e-6f || output.g != 0 || output.b != 0 || output.a != 1)
                    throw new Exception("HLSL 2021 template/operator/short-circuit probe failed: " + output);
        }
        finally
        {
            RenderTexture.active = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(readback);
        }
    }

    private static void CheckShader(Shader shader)
    {
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
            if (message.severity.ToString() == "Error") throw new Exception(message.message);
        if (!shader.isSupported) throw new Exception("Unsupported shader: " + shader.name);
    }

    private static Color[] Render(Material material, RenderTexture target, Texture2D readback)
    {
        using var command = new CommandBuffer();
        command.SetRenderTarget(target);
        command.ClearRenderTarget(false, true, Color.clear);
        command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
        Graphics.ExecuteCommandBuffer(command);
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
        readback.Apply();
        return readback.GetPixels();
    }
}
