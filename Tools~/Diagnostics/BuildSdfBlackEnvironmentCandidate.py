"""Build an in-memory trace candidate without changing the production asset."""
from pathlib import Path
import re


def build_source():
    folder=Path(__file__).resolve().parent;workspace=folder.parents[1]
    source=(workspace/'Shaders/Core/Private/GlobalIllumination/SdfScreenProbeTracing.compute').read_text(encoding='utf-8-sig')
    helper=(folder/'SdfBlackEnvironmentCandidate.hlsl.txt').read_text(encoding='utf-8-sig')
    if 'bool SdfKnownBlackEnvironmentMiss(' not in source:
        marker='bool TraceSdfGatherHit('
        assert source.count(marker)==1;source=source.replace(marker,helper+'\n'+marker,1)
        marker='            if (missState == RaySkyVisible)\n'
        assert source.count(marker)==1
        source=source.replace(marker,'''            else if (missState == RayMiss && !useSky
                && SdfKnownBlackEnvironmentMiss(origin, direction, tracedDistance, incomplete, screenState))
            {
                missState = RaySkyVisible;
            }
'''+marker,1)
    source=re.sub(r'#include "([^"/]+)"',lambda m:'#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/'+m[1]+'"',source)
    return source


def install_script(source):
    helper=Path(__file__).with_name('ScreenProbeExperimentalTracing.cs.txt').read_text(encoding='utf-8-sig')
    marker='    string arm = "";'
    assert source.count(marker)==1
    source=source.replace(marker,helper+'\n'+marker,1)
    return source
