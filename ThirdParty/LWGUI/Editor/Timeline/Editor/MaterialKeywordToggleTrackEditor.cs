// Copyright (c) Jason Ma

#if MATERIAL_KEYWORDS_TRACK_REQUIRES_TIMELINE

using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;
using LWGUI.Timeline;
using LWGUI.Runtime.Timeline;
using UnityEngine.Playables;

namespace LWGUI.Timeline
{
    // Editor used by the TimelineEditor to customize the view of a MaterialKeywordToggleTrack
    [CustomTimelineEditor(typeof(MaterialKeywordToggleTrack))]
    public class MaterialKeywordToggleTrackEditor : TrackEditor
    {
        public override void OnTrackChanged(TrackAsset track)
        {
            base.OnTrackChanged(track);

            if (!VersionControlHelper.IsWriteable(track))
                return;
            
            var targetToggleTrack = track as MaterialKeywordToggleTrack;
            if (targetToggleTrack == null || !targetToggleTrack.IsValid())
                return;
            
            var directors = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var director in directors)
            {
                var targetRenderer = director.GetGenericBinding(targetToggleTrack) as Renderer;
                var rootAnimator = director.GetGenericBinding(targetToggleTrack.srcAnimationTrack) as Animator;
                if (targetRenderer == null || rootAnimator == null)
                    continue;
                
                TimelineHelper.CopyAnimationCurveToMaterialKeywordToggleTrack(targetToggleTrack.srcAnimationTrack, targetToggleTrack.srcAnimationCurve, targetToggleTrack);
                break;
            }
        }
    }
}

#endif
