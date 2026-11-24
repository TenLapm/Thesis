import csv, json
F=['maze_length','breach_vulnerability','chokepoint_reliance','tower_concentration']
groups={'builder':{f:[] for f in F},'reactive':{f:[] for f in F},'human':{f:[] for f in F}}
for r in csv.DictReader(open('Results/2026-11-24_director-ranges/scripted-profiles.csv')):
    for f in F:
        if r[f]!='': groups[r['policy']][f].append(float(r[f]))
seen=set()
for l in open('Results/2026-11-24_director-ranges/student-playtest-telemetry.jsonl',encoding='utf-8'):
    row=json.loads(l)
    if row['wave']<2 or row['wave'] in seen or not row.get('profile'): continue
    seen.add(row['wave'])
    for f in F: groups['human'][f].append(row['profile'][f])
def q(v,p):
    v=sorted(v); 
    if not v: return None
    i=p*(len(v)-1); lo=int(i); hi=min(lo+1,len(v)-1); return v[lo]+(v[hi]-v[lo])*(i-lo)
for f in F:
    line=[f]
    lows=[];highs=[]
    for g in groups:
        v=groups[g][f]
        line.append(f"{g}: n={len(v)} 5%={q(v,.05):.3f} 95%={q(v,.95):.3f}")
        lows.append(q(v,.05)); highs.append(q(v,.95))
    print(' | '.join(line), ' => low', round(min(lows),3),'high',round(max(highs),3))
# pooled equal weight quantiles for chokepoint bins
f='chokepoint_reliance'
pts=[]
for g in groups:
    v=groups[g][f]; w=1/len(v)
    pts+= [(x,w) for x in v]
pts.sort(); tot=sum(w for _,w in pts)
edges=[]
for p in (.2,.4,.6,.8):
    acc=0
    for x,w in pts:
        acc+=w
        if acc>=p*tot: edges.append(round(x,3)); break
print('bin edges', edges)
