#!/usr/bin/env python3
"""Export standard PNG plots from analyze-dynamic.py JSON (no scene/image editing)."""
import argparse
import json
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

COLORS = {'fixed4':'#6c757d', 'fixed8':'#2864b4', 'adaptive':'#b33b35'}
LABELS = {'fixed4':'Fixed 4', 'fixed8':'Fixed 8', 'adaptive':'Adaptive 2/4'}


def plot(report, directory):
    if report.get('status') == 'invalid-dynamic-fixtures':
        raise ValueError('Invalid fixture data must not produce final dynamic-quality plots.')
    directory.mkdir(parents=True, exist_ok=True)
    scenarios = report['request']['scenarios']; modes = report['request']['modes']; x = np.arange(len(scenarios))
    width = .75 / len(modes)
    fig, axes = plt.subplots(2, 2, figsize=(15, 9), constrained_layout=True)
    fields = [('mae','Floor mean absolute error'),('positive_visibility_error','Floor positive error (under-occlusion)'),
              ('negative_visibility_error','Floor negative error (over-occlusion)'),('bias','Floor reference bias sensitivity')]
    for ax, (field,title) in zip(axes.flat,fields):
        for i, mode in enumerate(modes):
            values=[]
            for scenario in scenarios:
                case = report['cases'][scenario+'_'+mode]
                value = case['bias_sensitive_fraction_floor'] if field=='bias' else case['reference_bias_0_001'][field]
                values.append(np.nan if value is None else value*(100 if field=='bias' else 1))
            ax.bar(x+(i-(len(modes)-1)/2)*width, values, width, color=COLORS.get(mode), label=LABELS.get(mode,mode))
        ax.set_title(title,loc='left',weight='bold'); ax.set_xticks(x, [s.replace('_',' ') for s in scenarios],rotation=30,ha='right')
        ax.set_ylabel('Samples with |bias delta| > 0.05 (%)' if field=='bias' else 'Visibility error (0-1)')
        ax.grid(axis='y',alpha=.2); ax.set_axisbelow(True)
    axes[0,0].legend(frameon=False)
    fig.suptitle(f"Directional VSM / floor receiver baseline / {report['request']['referenceRays']}-ray finite RTAS reference",fontsize=15,weight='bold')
    fig.savefig(directory/'dynamic-floor-errors.png',dpi=180); plt.close(fig)

    fig, grid = plt.subplots(2,2,figsize=(15,9),constrained_layout=True)
    axes = grid.ravel()
    any_evaluated = any_detected = False
    maximum_missed = 0.0
    for i,mode in enumerate(modes):
        counts=[]; delays=[]; tails=[]; missed=[]
        for scenario in scenarios:
            dynamic=report['cases'][scenario+'_'+mode]['dynamic_response']
            count=dynamic.get('evaluated_events',0); counts.append(count)
            delay=dynamic.get('first_detection_frames_conditional_on_detection',{}).get('p95')
            delays.append(np.nan if delay is None else delay)
            tail=dynamic.get('first_detection_frames_conditional_on_detection',{}).get('p99')
            tails.append(np.nan if tail is None else tail)
            fraction=dynamic.get('not_detected_fraction')
            missed.append(np.nan if fraction is None else fraction*100)
            if fraction is not None: maximum_missed=max(maximum_missed,fraction*100)
        any_evaluated |= any(count > 0 for count in counts)
        any_detected |= bool(np.isfinite(delays).any())
        offset=x+(i-(len(modes)-1)/2)*width
        axes[0].bar(offset,counts,width,color=COLORS.get(mode),label=LABELS.get(mode,mode))
        axes[1].plot(offset,delays,'o',color=COLORS.get(mode),label=LABELS.get(mode,mode))
        axes[2].plot(offset,tails,'o',color=COLORS.get(mode),label=LABELS.get(mode,mode))
        axes[3].plot(offset,missed,'o',color=COLORS.get(mode),label=LABELS.get(mode,mode))
    for ax,title,ylabel in zip(axes,['Evaluated reference-change events','First detection: conditional P95','First detection: conditional P99','Not detected within full horizon'],['Event count (symlog)','Frames','Frames','Percent']):
        ax.set_title(title,loc='left',weight='bold');ax.set_ylabel(ylabel);ax.set_xticks(x,[s.replace('_',' ') for s in scenarios],rotation=30,ha='right')
        ax.set_xlim(-.5,len(scenarios)-.5)
        ax.grid(axis='y',alpha=.2);ax.set_axisbelow(True)
    axes[0].set_yscale('symlog',linthresh=1);axes[0].set_ylim(bottom=0);axes[0].legend(frameon=False)
    if not any_evaluated:
        axes[0].set_ylim(0,1)
        for ax in (axes[0],axes[3]): ax.text(.5,.5,'No qualifying events',transform=ax.transAxes,ha='center',color='#666666')
    if not any_detected:
        for ax in (axes[1],axes[2]): ax.text(.5,.5,'Not measured',transform=ax.transAxes,ha='center',color='#666666')
    for ax in (axes[1],axes[2]): ax.set_ylim(-.3,report['thresholds']['max_delay']+.5)
    axes[3].set_ylim(-.03,max(1,maximum_missed*1.1))
    fig.suptitle(f"Dynamic response / {report['request']['referenceRays']}-ray finite reference / jump >= {report['thresholds']['jump_threshold']:g}\nBias-consistent continuous world samples",fontsize=13,weight='bold')
    fig.supxlabel('Absent points mean not measured; zero qualified events are never a zero-lag pass. Camera motion correspondence is limited.',fontsize=10)
    fig.savefig(directory/'dynamic-response-events.png',dpi=180);plt.close(fig)

    if 'receiver_lift' in scenarios:
        fig,axes=plt.subplots(2,2,figsize=(12,8),constrained_layout=True)
        for mode in modes:
            case=report['cases']['receiver_lift_'+mode].get('moving_receiver_top')
            if case is None: continue
            frames=case['per_frame'];steps=[row['step'] for row in frames]
            values=[ [row['reference_bias_0_001']['mae'] for row in frames],
                [None if row['strong_error_fraction'] is None else row['strong_error_fraction']*100 for row in frames],
                [row['history_age']['median'] for row in frames], [row['visible_top_samples'] for row in frames] ]
            for ax,v in zip(axes.flat,values):ax.plot(steps,v,label=LABELS.get(mode,mode),color=COLORS.get(mode),linewidth=1.8)
        for ax,title,ylabel in zip(axes.flat,['Moving receiver top: MAE','Moving receiver top: strong error','Moving receiver top: history age','Visible top coverage'],['Visibility error','|Error| > 0.25 (%)','Median frames','Grid samples']):
            ax.set_title(title,loc='left',weight='bold');ax.set_xlabel('Capture step');ax.set_ylabel(ylabel);ax.grid(alpha=.2)
        axes[0,0].legend(frameon=False)
        fig.suptitle(f"Receiver lift / {report['request']['referenceRays']}-ray finite reference / exact top-plane ROI\nMetrics do not establish material-point latency",fontsize=13,weight='bold')
        fig.savefig(directory/'receiver-lift-top.png',dpi=180);plt.close(fig)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('report',type=Path);parser.add_argument('--output-directory',type=Path,required=True)
    args=parser.parse_args();plot(json.loads(args.report.read_text(encoding='utf-8')),args.output_directory)
