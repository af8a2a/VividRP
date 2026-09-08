from pathlib import Path
s=Path('Roadmap~/Experiments/VSMFineCoverage_20260908/VSMFineCoverageProbe.pool512.cs.txt').read_text()
a=s.index('        private float m_FocusDistance;');b=s.index('        private const int RoiX',a)
s=s[:a]+'''        private int m_OriginalPageLimit;
        private static readonly string[] Variants = { "pcf512", "smrt4x4", "smrt4x8", "smrt8x4", "smrt4x8angle1", "smrtZero" };
        private int VariantIndex => Mathf.Max(0, m_Stage) / 4;
        private int Rays => VariantIndex == 3 ? 8 : 4;
        private int Steps => VariantIndex == 1 || VariantIndex == 3 ? 4 : 8;
        private float Angle => VariantIndex == 4 ? 1 : VariantIndex == 5 ? 0 : .5f;
        private VividAdditionalLightData m_LightData;
        private float m_OriginalAngle;
''' + s[b:]
s=s.replace('VSMFineCoverage','VSMSMRT').replace('VSM Fine Coverage','VSM SMRT').replace('VSM fine coverage','VSM SMRT').replace('vsm-fine-coverage','vsm-smrt-captures')
s=s.replace('{ "static", "translate", "yaw" }','{ "static", "translate", "yaw", "light" }').replace('{ 0, 3, 4, 5, 6, 8 }','{ 0, 3, 4, 5, 6, 7 }')
s=s.replace('RunFromStage(-1)','RunFromStage(0)')
s=s.replace('m_Stage % 3 == 0 ? 32 : m_Stage % 3 == 1 ? 96 : 128','m_Stage % 4 == 0 ? 32 : m_Stage % 4 == 1 ? 96 : 128').replace('Variants[m_Stage / 3]','Variants[VariantIndex]').replace('        private float Scale => Scales[Mathf.Max(0, m_Stage) / 3];\n','').replace('Scenarios[Mathf.Max(0, m_Stage) % 3]','Scenarios[Mathf.Max(0, m_Stage) % 4]')
a=s.index('            m_OriginalScale =');b=s.index('            var mask =',a);s=s[:a]+s[b:]
s=s.replace('            VirtualShadowMapReceiverQuality.EditorCoverageTransitionScale = Scale;\n            m_HasOrigins = false;\n            m_FocusEnabled = m_Stage >= 3;','''            m_Overrides.virtualShadowMapSMRT.Override(VariantIndex != 0);
            m_Overrides.virtualShadowMapSMRTRayCount.Override(Rays);
            m_Overrides.virtualShadowMapSMRTSamplesPerRay.Override(Steps);
            m_Overrides.virtualShadowMapSMRTMaxRayLength.Override(10);
            if (m_LightData != null) m_LightData.angularDiameter = Angle;''')
s=s.replace('                && VirtualShadowMapReceiverQuality.BuildParameters(settings).z == Scale','                && settings.virtualShadowMapSMRT.value == (VariantIndex != 0)')
s=s.replace('public float coverageScale, coverageTransition;','public float angle; public int rays, steps; public bool smrt;')
s=s.replace('coverageScale = Scale, coverageTransition = .2f * (Scale > 0 ? Scale : 1),','angle = Angle, rays = Rays, steps = Steps, smrt = VariantIndex != 0,')
s=s.replace('                m_Light = light; m_LightRotation = light.transform.localRotation;','''                m_Light = light; m_LightRotation = light.transform.localRotation;
                m_LightData = light.GetComponent<VividAdditionalLightData>();
                if (m_LightData == null) { m_Error = "Missing additional light data."; return; }
                m_OriginalAngle = m_LightData.angularDiameter; m_LightData.angularDiameter = Angle;''')
s=s.replace('            camera.transform.SetPositionAndRotation(position, rotation);','''            if (m_Light != null && Scenario == "light")
                m_Light.transform.localRotation = m_Measuring && m_Step < 64
                    ? Quaternion.Euler(0, 3 * Mathf.Sin(Mathf.PI * m_Step / 63f), 0) * m_LightRotation : m_LightRotation;
            camera.transform.SetPositionAndRotation(position, rotation);''')
s=s.replace('(Scenario == "translate" ? 64 : 96)','(Scenario == "translate" || Scenario == "light" ? 64 : 96)')
s=s.replace('            cmd.SetComputeVectorParam(m_Shader, "_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(settings));','''            cmd.SetComputeVectorParam(m_Shader, "_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(settings));
            cmd.SetComputeVectorParam(m_Shader, "_VSMSMRTParameters", VirtualShadowMapReceiverQuality.BuildSMRTParameters(settings, m_LightData.angularDiameter));''')
a=s.index('                if (path.EndsWith("_debug7');b=s.index('                byte[] bytes',a);s=s[:a]+s[b:]
s=s.replace('if (++m_Stage < 9)','if (++m_Stage < Variants.Length * 4)')
a=s.index('            VirtualShadowMapReceiverQuality.EditorCoverageTransitionScale = m_OriginalScale;');b=s.index('            VirtualShadowMapPrototypeRuntime.EditorPhysicalPageLimit',a);s=s[:a]+s[b:]
s=s.replace('            if (m_Light != null) m_Light.transform.localRotation = m_LightRotation;\n            Application.runInBackground', '            if (m_Light != null) m_Light.transform.localRotation = m_LightRotation;\n            if (m_LightData != null) m_LightData.angularDiameter = m_OriginalAngle;\n            Application.runInBackground')
s=s.replace('Priority allocator HEAD 4c39274a. 4096/512 pages. Camera layout, bounded L3 .2, bounded L3 .05 coverage transition (LOD width .2). Empty pool each stage, >=128 warmup, first jitter aligned. Static32, translate96 (64 move,32 rest), yaw128 (96 move,32 rest). Current AA, fixed exposure12.252064. Captures are not performance measurements.','4096/512 pages. Unchanged concentric layout and transition .2. PCF, 4x4, 4x8, 8x4, angle1, angle0. Empty pools and 128+ warm frames, aligned first jitter. Static32, translate96, yaw128, moving light128 (64 move,64 rest). Current TSR, fixed exposure12.252064. Readbacks are not performance timings.')
Path('Editor/Tools/VSMSMRTProbe.cs').write_text(s)
s=Path('Roadmap~/Experiments/VSMFineCoverage_20260908/VSMFineCoverageAudit.compute.txt').read_text().replace('VSMFineCoverageAudit','VSMSMRTAudit')
s=s.replace('    g_VSMDebugQuality = -1;','    g_VSMDebugQuality = -1;\n    g_VSMDebugSMRT = 0;')
a=s.index('    if (_VSMReceiverDebugMode == 7)');b=s.index('    float3 normal',a);s=s[:a]+s[b:]
s=s.replace('if (_VSMReceiverDebugMode == 8)','if (_VSMReceiverDebugMode == 7)').replace('data = g_VSMDebugTransition;','data = float4(g_VSMDebugSMRT);')
Path('Editor/Tools/VSMSMRTAudit.compute').write_text(s)
