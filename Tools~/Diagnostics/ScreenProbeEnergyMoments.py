"""Owned native experiment: moments and wavelet guides in consumed-energy space."""


def temporal_source(source):
    replacements={
        'DecodeScreenProbeCoveredHistory(unscaledRgb, ageAndCoverage.y) / max(ageAndCoverage.y, 1e-5)':
            'DecodeScreenProbeCoveredHistory(unscaledRgb, ageAndCoverage.y)',
        'DecodeScreenProbeCoveredHistory(sample.rgb, ageAndCoverage.y) / max(ageAndCoverage.y, 1e-5)':
            'DecodeScreenProbeCoveredHistory(sample.rgb, ageAndCoverage.y)',
        'float combinedWeight = history.momentCoverage + covered;':'float combinedWeight = history.momentCoverage + weight;',
        'float fraction = covered / max(combinedWeight, 1e-5);':'float fraction = weight / max(combinedWeight, 1e-5);',
        'history.logVariance += covered * variance':'history.logVariance += weight * variance',
        'history.momentAge += covered * moments.z;':'history.momentAge += weight * moments.z;',
        'history.momentCoverage += covered;':'history.momentCoverage += weight;',
        'history.momentCoverage = momentWeight / max(coveredWeight, 1e-5);':
            'history.momentCoverage = momentWeight / max(history.footprint, 1e-5);',
        'float currentLog = ScreenProbeLogLuminance(result.rgb);':
            'float currentLog = ScreenProbeLogLuminance(result.rgb * result.a);',
        'ScreenProbeLogLuminance(rectified) - ScreenProbeLogLuminance(historyLighting)':
            'ScreenProbeLogLuminance(rectified * history.coverage) - ScreenProbeLogLuminance(historyLighting * history.coverage)',
    }
    for before,after in replacements.items():
        assert source.count(before)==1,before
        source=source.replace(before,after,1)
    return source


def wavelet_source(source):
    before='SdfAtrousLogLuminance(center.rgb)';assert source.count(before)==1
    source=source.replace(before,'SdfAtrousLogLuminance(center.rgb * abs(center.a))',1)
    before='SdfAtrousLogLuminance(lighting.rgb)';assert source.count(before)==2
    return source.replace(before,'SdfAtrousLogLuminance(lighting.rgb * abs(lighting.a))')


HELPER='''    ComputeShader energyOriginalTemporal=null;bool energyTemporalInstalled=false;
    void SetupEnergyMoments() {
        if(privateMethod!="none")throw new InvalidOperationException("Energy moments require an independent clean control.");
        var field=type.GetField("m_GatherTemporalCompute",inst);
        energyOriginalTemporal=(ComputeShader)field.GetValue(privatePass);
        privateTemporalCandidate=PrivateShader("energy-temporal.compute.txt",new[]{"ScreenProbeTemporalReprojectionWithMomentsCS"});
        foreach(string name in new[]{"TemporalSdfDiffuseGI","ScreenProbeTemporalReprojectionCS","ScreenProbeTemporalReprojectionWithMomentsCS",
            "TemporalSdfRestirDiffuseGI","TemporalSdfRestirVariance","InspectSdfHistoryQuantizationCS","InspectSdfCoveredHistoryEnergyCS","InspectScreenProbeTemporalHistoryCS"})
            if(energyOriginalTemporal.FindKernel(name)!=privateTemporalCandidate.FindKernel(name))throw new InvalidOperationException("Temporal kernel layout changed.");
        field.SetValue(privatePass,privateTemporalCandidate);energyTemporalInstalled=true;
    }
    void CleanupEnergyMoments() {
        if(!energyTemporalInstalled)return;
        var field=type.GetField("m_GatherTemporalCompute",inst);
        if(object.ReferenceEquals(field.GetValue(privatePass),privateTemporalCandidate))field.SetValue(privatePass,energyOriginalTemporal);
        else errors.Add("Temporal shader changed externally; original not overwritten.");
        energyTemporalInstalled=false;
    }
'''


def install(source,shaders):
    shaders['energy-temporal.compute.txt']=temporal_source(shaders['temporal.compute.txt'])
    for name in ('quadrature-gather.compute.txt','covered-wavelet.compute.txt'):
        shaders[name]=wavelet_source(shaders[name])
    split=HELPER.index('    void SetupEnergyMoments(')
    replacements={
        '    string privateMethod=':HELPER[:split]+'\n    string privateMethod=',
        '    object PrivateField(string name)':HELPER[split:]+'\n    object PrivateField(string name)',
        'SetupQuadratureNormalization();SetupOraclePixels();':'SetupQuadratureNormalization();SetupOraclePixels();SetupEnergyMoments();',
        'oraclePixelMap?.Dispose();CleanupPrivateReplay();':'CleanupEnergyMoments();oraclePixelMap?.Dispose();CleanupPrivateReplay();',
    }
    for before,after in replacements.items():
        assert source.count(before)==1,before
        source=source.replace(before,after,1)
    return source,shaders
