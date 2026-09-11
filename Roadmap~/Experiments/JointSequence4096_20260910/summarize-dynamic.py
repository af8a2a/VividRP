from pathlib import Path
import json,numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
p=Path("Temp~/joint-sequence/dynamic-analysis");s=json.loads((p/"summary.json").read_text());regions=["roof","ledge","arches"]
b=s["stages"]["bnd_static"];c=s["stages"]["stbn_shared_joint_static"]
r={"static_window":[32,543],"repeat_output_change_percent":{n:100*(c["temporal"]["output"][n]["same_jitter_rms"]/b["temporal"]["output"][n]["same_jitter_rms"]-1) for n in regions},"dynamic":{}}
fig,axs=plt.subplots(2,3,figsize=(13,6),sharex=True)
for i,scenario in enumerate(["light","translate"]):
 r["dynamic"][scenario]={}
 for variant in ["bnd","stbn_shared_joint"]:
  a=s["stages"][variant+"_"+scenario];rows=a["post_stop"];o={"all_post_stop_parameter_matches":all(all(x["matches"].values()) for x in rows),"overflow_max":a["overflow_max"],"missing_max":a["missing_max"],"regions":{}}
  for j,n in enumerate(regions):
   reg={}
   for k in ["shadow","source","output"]:
    reg[k]={"first32_mean_rms":float(np.mean([x["error"][k][n] for x in rows if x["after_stop"]<32])),"first64_mean_rms":float(np.mean([x["error"][k][n] for x in rows if x["after_stop"]<64])),"tail64_mean_rms":float(np.mean([x["error"][k][n] for x in rows if x["after_stop"]>=416])),"max_rms":max(x["error"][k][n] for x in rows)}
   reg["sampled_output_t10"]=next((x["after_stop"] for x in rows if x["error"]["output"][n] <= .1*rows[0]["error"]["output"][n]),None)
   o["regions"][n]=reg
   shown=[x for x in rows if x["after_stop"]<=96];axs[i,j].semilogy([x["after_stop"] for x in shown],[x["error"]["output"][n] for x in shown],label=variant.replace("_"," "));axs[i,j].set_title(scenario+" / "+n);axs[i,j].grid(alpha=.2);axs[i,j].set_xlabel("Frames after restoration")
  r["dynamic"][scenario][variant]=o
 for n in regions:
  a=r["dynamic"][scenario]["stbn_shared_joint"]["regions"][n]["output"];b=r["dynamic"][scenario]["bnd"]["regions"][n]["output"];a["first32_vs_bnd_change_percent"]=100*(a["first32_mean_rms"]/b["first32_mean_rms"]-1)
axs[0,0].legend(fontsize=8)
for ax in axs[:,0]:ax.set_ylabel("Output luma RMS vs paired static")
fig.tight_layout();fig.savefig(p/"dynamic-response.png",dpi=160);(p/"decision.json").write_text(json.dumps(r,indent=2));print(json.dumps(r,indent=2))
