#!/usr/bin/env python3
"""Regenerate Assets/GuateGeeks/Runtime/AwsIconGeometry.Data.cs from the official AWS Architecture Service Icons.

Standard library only:  python3 Tools/Generate-AwsIconGeometry.py
Parses the white symbol path of each 64 px SVG (80x80 viewBox), flattens Bezier/arc segments to a 0.035-unit
tolerance, resolves the even-odd fill into outer polygons with holes and triangulates them with a port of
mapbox/earcut (ISC licence). It prints triangle-vs-polygon area per icon as a completeness check.
"""
# Port of mapbox/earcut (ISC license) core triangulation with holes.
import math
class Node:
    __slots__=('i','x','y','prev','next','z','prevZ','nextZ','steiner')
    def __init__(s,i,x,y):
        s.i=i;s.x=x;s.y=y;s.prev=s.next=None;s.z=0;s.prevZ=s.nextZ=None;s.steiner=False
def earcut(data, holeIndices=None, dim=2):
    hasHoles=bool(holeIndices); outerLen=holeIndices[0]*dim if hasHoles else len(data)
    outerNode=linkedList(data,0,outerLen,dim,True); triangles=[]
    if outerNode is None or outerNode.next is outerNode.prev: return triangles
    if hasHoles: outerNode=eliminateHoles(data,holeIndices,outerNode,dim)
    minX=minY=invSize=0
    if len(data)>80*dim:
        xs=data[0:outerLen:dim]; ys=data[1:outerLen:dim]
        minX=min(xs);minY=min(ys);maxX=max(xs);maxY=max(ys)
        invSize=max(maxX-minX,maxY-minY); invSize=32767/invSize if invSize!=0 else 0
    earcutLinked(outerNode,triangles,dim,minX,minY,invSize,0)
    return triangles
def linkedList(data,start,end,dim,clockwise):
    last=None
    if clockwise==(signedArea(data,start,end,dim)>0):
        for i in range(start,end,dim): last=insertNode(i,data[i],data[i+1],last)
    else:
        for i in range(end-dim,start-1,-dim): last=insertNode(i,data[i],data[i+1],last)
    if last and equals(last,last.next): removeNode(last); last=last.next
    return last
def filterPoints(start,end=None):
    if not start: return start
    if not end: end=start
    p=start
    while True:
        again=False
        if not p.steiner and (equals(p,p.next) or area(p.prev,p,p.next)==0):
            removeNode(p); p=end=p.prev
            if p is p.next: break
            again=True
        else: p=p.next
        if not (again or p is not end): break
    return end
def earcutLinked(ear,triangles,dim,minX,minY,invSize,pas):
    if not ear: return
    if not pas and invSize: indexCurve(ear,minX,minY,invSize)
    stop=ear
    while ear.prev is not ear.next:
        prev=ear.prev; nxt=ear.next
        if (isEarHashed(ear,minX,minY,invSize) if invSize else isEar(ear)):
            triangles+= [prev.i//dim, ear.i//dim, nxt.i//dim]
            removeNode(ear); ear=nxt.next; stop=nxt.next; continue
        ear=nxt
        if ear is stop:
            if not pas: earcutLinked(filterPoints(ear),triangles,dim,minX,minY,invSize,1)
            elif pas==1:
                ear=cureLocalIntersections(filterPoints(ear),triangles,dim)
                earcutLinked(ear,triangles,dim,minX,minY,invSize,2)
            elif pas==2: splitEarcut(ear,triangles,dim,minX,minY,invSize)
            break
def isEar(ear):
    a=ear.prev;b=ear;c=ear.next
    if area(a,b,c)>=0: return False
    p=ear.next.next
    while p is not ear.prev:
        if pointInTriangle(a.x,a.y,b.x,b.y,c.x,c.y,p.x,p.y) and area(p.prev,p,p.next)>=0: return False
        p=p.next
    return True
def isEarHashed(ear,minX,minY,invSize):
    a=ear.prev;b=ear;c=ear.next
    if area(a,b,c)>=0: return False
    x0=min(a.x,b.x,c.x);y0=min(a.y,b.y,c.y);x1=max(a.x,b.x,c.x);y1=max(a.y,b.y,c.y)
    minZ=zOrder(x0,y0,minX,minY,invSize); maxZ=zOrder(x1,y1,minX,minY,invSize)
    p=ear.prevZ;n=ear.nextZ
    while p and p.z>=minZ and n and n.z<=maxZ:
        if p is not a and p is not c and pointInTriangle(a.x,a.y,b.x,b.y,c.x,c.y,p.x,p.y) and area(p.prev,p,p.next)>=0: return False
        p=p.prevZ
        if n is not a and n is not c and pointInTriangle(a.x,a.y,b.x,b.y,c.x,c.y,n.x,n.y) and area(n.prev,n,n.next)>=0: return False
        n=n.nextZ
    while p and p.z>=minZ:
        if p is not a and p is not c and pointInTriangle(a.x,a.y,b.x,b.y,c.x,c.y,p.x,p.y) and area(p.prev,p,p.next)>=0: return False
        p=p.prevZ
    while n and n.z<=maxZ:
        if n is not a and n is not c and pointInTriangle(a.x,a.y,b.x,b.y,c.x,c.y,n.x,n.y) and area(n.prev,n,n.next)>=0: return False
        n=n.nextZ
    return True
def cureLocalIntersections(start,triangles,dim):
    p=start
    while True:
        a=p.prev;b=p.next.next
        if not equals(a,b) and intersects(a,p,p.next,b) and locallyInside(a,b) and locallyInside(b,a):
            triangles+=[a.i//dim,p.i//dim,b.i//dim]
            removeNode(p);removeNode(p.next);p=start=b
        p=p.next
        if p is start: break
    return filterPoints(p)
def splitEarcut(start,triangles,dim,minX,minY,invSize):
    a=start
    while True:
        b=a.next.next
        while b is not a.prev:
            if a.i!=b.i and isValidDiagonal(a,b):
                c=splitPolygon(a,b); a=filterPoints(a,a.next); c=filterPoints(c,c.next)
                earcutLinked(a,triangles,dim,minX,minY,invSize,0); earcutLinked(c,triangles,dim,minX,minY,invSize,0); return
            b=b.next
        a=a.next
        if a is start: break
def eliminateHoles(data,holeIndices,outerNode,dim):
    queue=[]
    for i in range(len(holeIndices)):
        start=holeIndices[i]*dim; end=holeIndices[i+1]*dim if i<len(holeIndices)-1 else len(data)
        lst=linkedList(data,start,end,dim,False)
        if lst is lst.next: lst.steiner=True
        queue.append(getLeftmost(lst))
    queue.sort(key=lambda n:(n.x,n.y))
    for q in queue: outerNode=eliminateHole(q,outerNode)
    return outerNode
def eliminateHole(hole,outerNode):
    bridge=findHoleBridge(hole,outerNode)
    if not bridge: return outerNode
    bridgeReverse=splitPolygon(bridge,hole)
    filterPoints(bridgeReverse,bridgeReverse.next)
    return filterPoints(bridge,bridge.next)
def findHoleBridge(hole,outerNode):
    p=outerNode;hx=hole.x;hy=hole.y;qx=-math.inf;m=None
    while True:
        if hy<=p.y and hy>=p.next.y and p.next.y!=p.y:
            x=p.x+(hy-p.y)*(p.next.x-p.x)/(p.next.y-p.y)
            if x<=hx and x>qx:
                qx=x; m=p if p.x<p.next.x else p.next
                if x==hx: return m
        p=p.next
        if p is outerNode: break
    if not m: return None
    stop=m;mx=m.x;my=m.y;tanMin=math.inf;p=m
    while True:
        if hx>=p.x and p.x>=mx and hx!=p.x and pointInTriangle(hx if hy<my else qx,hy,mx,my,qx if hy<my else hx,hy,p.x,p.y):
            tan=abs(hy-p.y)/(hx-p.x)
            if locallyInside(p,hole) and (tan<tanMin or (tan==tanMin and (p.x>m.x or (p.x==m.x and sectorContainsSector(m,p))))):
                m=p;tanMin=tan
        p=p.next
        if p is stop: break
    return m
def sectorContainsSector(m,p): return area(m.prev,m,p.prev)<0 and area(p.next,m,m.next)<0
def indexCurve(start,minX,minY,invSize):
    p=start
    while True:
        if p.z==0: p.z=zOrder(p.x,p.y,minX,minY,invSize)
        p.prevZ=p.prev;p.nextZ=p.next;p=p.next
        if p is start: break
    p.prevZ.nextZ=None;p.prevZ=None
    sortLinked(p)
def sortLinked(lst):
    inSize=1
    while True:
        p=lst;lst=None;tail=None;numMerges=0
        while p:
            numMerges+=1;q=p;pSize=0
            for i in range(inSize):
                pSize+=1;q=q.nextZ
                if not q: break
            qSize=inSize
            while pSize>0 or (qSize>0 and q):
                if pSize!=0 and (qSize==0 or not q or p.z<=q.z): e=p;p=p.nextZ;pSize-=1
                else: e=q;q=q.nextZ;qSize-=1
                if tail: tail.nextZ=e
                else: lst=e
                e.prevZ=tail;tail=e
            p=q
        tail.nextZ=None;inSize*=2
        if numMerges<=1: break
    return lst
def zOrder(x,y,minX,minY,invSize):
    x=int((x-minX)*invSize)&0xFFFFFFFF;y=int((y-minY)*invSize)&0xFFFFFFFF
    x=(x|(x<<8))&0x00FF00FF;x=(x|(x<<4))&0x0F0F0F0F;x=(x|(x<<2))&0x33333333;x=(x|(x<<1))&0x55555555
    y=(y|(y<<8))&0x00FF00FF;y=(y|(y<<4))&0x0F0F0F0F;y=(y|(y<<2))&0x33333333;y=(y|(y<<1))&0x55555555
    return x|(y<<1)
def getLeftmost(start):
    p=start;leftmost=start
    while True:
        if p.x<leftmost.x or (p.x==leftmost.x and p.y<leftmost.y): leftmost=p
        p=p.next
        if p is start: break
    return leftmost
def pointInTriangle(ax,ay,bx,by,cx,cy,px,py):
    return (cx-px)*(ay-py)>=(ax-px)*(cy-py) and (ax-px)*(by-py)>=(bx-px)*(ay-py) and (bx-px)*(cy-py)>=(cx-px)*(by-py)
def isValidDiagonal(a,b):
    return a.next.i!=b.i and a.prev.i!=b.i and not intersectsPolygon(a,b) and \
        ((locallyInside(a,b) and locallyInside(b,a) and middleInside(a,b) and (area(a.prev,a,b.prev) or area(a,b.prev,b))) or
         (equals(a,b) and area(a.prev,a,a.next)>0 and area(b.prev,b,b.next)>0))
def area(p,q,r): return (q.y-p.y)*(r.x-q.x)-(q.x-p.x)*(r.y-q.y)
def equals(p1,p2): return p1.x==p2.x and p1.y==p2.y
def intersects(p1,q1,p2,q2):
    o1=sign(area(p1,q1,p2));o2=sign(area(p1,q1,q2));o3=sign(area(p2,q2,p1));o4=sign(area(p2,q2,q1))
    if o1!=o2 and o3!=o4: return True
    if o1==0 and onSegment(p1,p2,q1): return True
    if o2==0 and onSegment(p1,q2,q1): return True
    if o3==0 and onSegment(p2,p1,q2): return True
    if o4==0 and onSegment(p2,q1,q2): return True
    return False
def onSegment(p,q,r): return q.x<=max(p.x,r.x) and q.x>=min(p.x,r.x) and q.y<=max(p.y,r.y) and q.y>=min(p.y,r.y)
def sign(n): return 1 if n>0 else -1 if n<0 else 0
def intersectsPolygon(a,b):
    p=a
    while True:
        if p.i!=a.i and p.next.i!=a.i and p.i!=b.i and p.next.i!=b.i and intersects(p,p.next,a,b): return True
        p=p.next
        if p is a: break
    return False
def locallyInside(a,b):
    return (area(a,b,a.next)>=0 and area(a,a.prev,b)>=0) if area(a.prev,a,a.next)<0 else (area(a,b,a.prev)<0 or area(a,a.next,b)<0)
def middleInside(a,b):
    p=a;inside=False;px=(a.x+b.x)/2;py=(a.y+b.y)/2
    while True:
        if ((p.y>py)!=(p.next.y>py)) and p.next.y!=p.y and (px<(p.next.x-p.x)*(py-p.y)/(p.next.y-p.y)+p.x): inside=not inside
        p=p.next
        if p is a: break
    return inside
def splitPolygon(a,b):
    a2=Node(a.i,a.x,a.y);b2=Node(b.i,b.x,b.y);an=a.next;bp=b.prev
    a.next=b;b.prev=a;a2.next=an;an.prev=a2;b2.next=a2;a2.prev=b2;bp.next=b2;b2.prev=bp
    return b2
def insertNode(i,x,y,last):
    p=Node(i,x,y)
    if not last: p.prev=p;p.next=p
    else: p.next=last.next;p.prev=last;last.next.prev=p;last.next=p
    return p
def removeNode(p):
    p.next.prev=p.prev;p.prev.next=p.next
    if p.prevZ: p.prevZ.nextZ=p.nextZ
    if p.nextZ: p.nextZ.prevZ=p.prevZ
def signedArea(data,start,end,dim):
    s=0;j=end-dim
    for i in range(start,end,dim): s+=(data[j]-data[i])*(data[i+1]+data[j+1]);j=i
    return s

# --- SVG parsing ---
import re, math, json, sys, xml.etree.ElementTree as ET
import os
ROOT=os.path.abspath(os.path.join(os.path.dirname(__file__),'..'))
SRC=os.path.join(ROOT,'aws-icons','Architecture-Service-Icons_07312026')+os.sep
ICONS={'ApiGateway':'Arch_Networking-Content-Delivery/64/Arch_Amazon-API-Gateway_64.svg','Lambda':'Arch_Compute/64/Arch_AWS-Lambda_64.svg',
 'DynamoDB':'Arch_Databases/64/Arch_Amazon-DynamoDB_64.svg','S3':'Arch_Storage/64/Arch_Amazon-Simple-Storage-Service_64.svg',
 'SQS':'Arch_Application-Integration/64/Arch_Amazon-Simple-Queue-Service_64.svg','EventBridge':'Arch_Application-Integration/64/Arch_Amazon-EventBridge_64.svg',
 'CloudWatch':'Arch_Management-Tools/64/Arch_Amazon-CloudWatch_64.svg'}
TOL=0.035  # flattening tolerance in the 80-unit viewBox (≈0.13 mm on a 30 cm tile)
def tokens(d):
    for m in re.finditer(r'[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?',d): yield m.group()
def bez_points(p0,p1,p2,p3):
    est=math.dist(p0,p1)+math.dist(p1,p2)+math.dist(p2,p3)
    n=max(2,int(math.ceil(math.sqrt(est/ (8*TOL)) * 2)))
    out=[]
    for i in range(1,n+1):
        t=i/n;u=1-t
        out.append((u**3*p0[0]+3*u*u*t*p1[0]+3*u*t*t*p2[0]+t**3*p3[0], u**3*p0[1]+3*u*u*t*p1[1]+3*u*t*t*p2[1]+t**3*p3[1]))
    return out
def arc_points(p0,rx,ry,phi,fa,fs,p1):
    if rx==0 or ry==0: return [p1]
    phi=math.radians(phi);c=math.cos(phi);s=math.sin(phi)
    dx=(p0[0]-p1[0])/2;dy=(p0[1]-p1[1])/2;x1=c*dx+s*dy;y1=-s*dx+c*dy
    rx=abs(rx);ry=abs(ry);lam=x1*x1/(rx*rx)+y1*y1/(ry*ry)
    if lam>1: rx*=math.sqrt(lam);ry*=math.sqrt(lam)
    num=rx*rx*ry*ry-rx*rx*y1*y1-ry*ry*x1*x1;den=rx*rx*y1*y1+ry*ry*x1*x1
    co=math.sqrt(max(0,num/den))*(-1 if fa==fs else 1)
    cx1=co*rx*y1/ry;cy1=-co*ry*x1/rx
    cx=c*cx1-s*cy1+(p0[0]+p1[0])/2;cy=s*cx1+c*cy1+(p0[1]+p1[1])/2
    def ang(ux,uy,vx,vy): return math.atan2(ux*vy-uy*vx,ux*vx+uy*vy)
    t1=ang(1,0,(x1-cx1)/rx,(y1-cy1)/ry);dt=ang((x1-cx1)/rx,(y1-cy1)/ry,(-x1-cx1)/rx,(-y1-cy1)/ry)
    if not fs and dt>0: dt-=2*math.pi
    if fs and dt<0: dt+=2*math.pi
    n=max(4,int(abs(dt)*max(rx,ry)/0.4)+1);out=[]
    for i in range(1,n+1):
        a=t1+dt*i/n;out.append((cx+rx*math.cos(a)*c-ry*math.sin(a)*s,cy+rx*math.cos(a)*s+ry*math.sin(a)*c))
    return out
def parse_path(d,tx=0,ty=0):
    t=list(tokens(d));i=0;cmd=None;cur=(0,0);start=(0,0);last_c=None;last_q=None;contours=[];pts=[]
    def num():
        nonlocal i; v=float(t[i]); i+=1; return v
    def close():
        nonlocal pts
        if len(pts)>2: contours.append(pts)
        pts=[]
    while i<len(t):
        if re.match(r'[A-Za-z]',t[i]): cmd=t[i];i+=1
        rel=cmd.islower();C=cmd.upper();ox,oy=(cur if rel else (0,0))
        if C=='Z':
            close();cur=start;last_c=last_q=None;continue
        if C=='M':
            close();cur=(num()+ox,num()+oy);start=cur;pts=[cur];cmd='l' if rel else 'L';last_c=last_q=None;continue
        if C=='L': cur=(num()+ox,num()+oy);pts.append(cur);last_c=last_q=None
        elif C=='H': cur=(num()+ox,cur[1]);pts.append(cur);last_c=last_q=None
        elif C=='V': cur=(cur[0],num()+oy);pts.append(cur);last_c=last_q=None
        elif C=='C':
            p1=(num()+ox,num()+oy);p2=(num()+ox,num()+oy);p3=(num()+ox,num()+oy)
            pts+=bez_points(cur,p1,p2,p3);last_c=p2;cur=p3;last_q=None
        elif C=='S':
            p1=(2*cur[0]-last_c[0],2*cur[1]-last_c[1]) if last_c else cur
            p2=(num()+ox,num()+oy);p3=(num()+ox,num()+oy);pts+=bez_points(cur,p1,p2,p3);last_c=p2;cur=p3;last_q=None
        elif C in 'QT':
            if C=='Q': q=(num()+ox,num()+oy)
            else: q=(2*cur[0]-last_q[0],2*cur[1]-last_q[1]) if last_q else cur
            p3=(num()+ox,num()+oy)
            pts+=bez_points(cur,(cur[0]+2/3*(q[0]-cur[0]),cur[1]+2/3*(q[1]-cur[1])),(p3[0]+2/3*(q[0]-p3[0]),p3[1]+2/3*(q[1]-p3[1])),p3)
            last_q=q;cur=p3;last_c=None
        elif C=='A':
            rx=num();ry=num();phi=num();fa=int(num());fs=int(num());p3=(num()+ox,num()+oy)
            pts+=arc_points(cur,rx,ry,phi,fa,fs,p3);cur=p3;last_c=last_q=None
    close()
    out=[]
    for c in contours:
        c=[(x+tx,y+ty) for x,y in c]
        clean=[]
        for p in c:
            if not clean or math.dist(p,clean[-1])>1e-4: clean.append(p)
        if math.dist(clean[0],clean[-1])<1e-4: clean.pop()
        if len(clean)>=3 and abs(sarea(clean))>1e-6: out.append(clean)
    return out
def sarea(c): return sum(c[i-1][0]*c[i][1]-c[i][0]*c[i-1][1] for i in range(len(c)))/2
def inside(pt,poly):
    x,y=pt;r=False;j=len(poly)-1
    for i in range(len(poly)):
        xi,yi=poly[i];xj,yj=poly[j]
        if (yi>y)!=(yj>y) and x<(xj-xi)*(y-yi)/(yj-yi)+xi: r=not r
        j=i
    return r
def probe(c):
    # A point just inside the contour near its first edge midpoint (robust against shared vertices).
    a,b=c[0],c[1];mx,my=(a[0]+b[0])/2,(a[1]+b[1])/2;nx,ny=-(b[1]-a[1]),b[0]-a[0];l=math.hypot(nx,ny) or 1
    for s in (1e-3,-1e-3):
        p=(mx+nx/l*s,my+ny/l*s)
        if inside(p,c): return p
    return (mx,my)
def convert(kind,path):
    root=ET.parse(SRC+path).getroot();ns='{http://www.w3.org/2000/svg}'
    bg=None;contours=[]
    def walk(e,tx,ty,fill):
        nonlocal bg
        tr=e.get('transform');
        if tr:
            m=re.match(r'translate\(([-\d.]+)[ ,]+([-\d.]+)\)',tr); tx+=float(m.group(1)); ty+=float(m.group(2))
        fill=e.get('fill',fill)
        tag=e.tag.replace(ns,'')
        if tag=='rect' and fill not in ('none',None) and fill.upper() not in('#FFFFFF','WHITE'): bg=fill
        if tag=='path' and fill and fill.upper() in ('#FFFFFF','WHITE'): contours.extend([parse_path(e.get('d'),tx,ty)])
        for ch in e: walk(ch,tx,ty,fill)
    walk(root,0,0,None)
    polys=[]
    for group in contours:  # evenodd within each path element
        depth=[sum(1 for j,o in enumerate(group) if j!=i and inside(probe(c),o)) for i,c in enumerate(group)]
        for i,c in enumerate(group):
            if depth[i]%2: continue
            holes=[h for j,h in enumerate(group) if depth[j]==depth[i]+1 and inside(probe(h),c)]
            polys.append((c,holes))
    verts=[];tris=[];loops=[]
    for outer,holes in polys:
        if sarea(outer)<0: outer=outer[::-1]
        hs=[h if sarea(h)<0 else h[::-1] for h in holes]
        flat=[v for p in outer for v in p];hi=[];n=len(outer)
        for h in hs: hi.append(n);flat+=[v for p in h for v in p];n+=len(h)
        t=earcut(flat,hi)
        base=len(verts)
        pts=outer+[p for h in hs for p in h]
        verts+=pts;tris+=[base+k for k in t]
        loops.append((base,len(outer)));o=base+len(outer)
        for h in hs: loops.append((o,len(h)));o+=len(h)
    return bg,verts,tris,loops,polys

# --- Conversion ---
res={}
for k,p in ICONS.items():
    bg,v,t,l,polys=convert(k,p)
    tri_area=sum(abs((v[t[i+1]][0]-v[t[i]][0])*(v[t[i+2]][1]-v[t[i]][1])-(v[t[i+2]][0]-v[t[i]][0])*(v[t[i+1]][1]-v[t[i]][1]))/2 for i in range(0,len(t),3))
    poly_area=sum(abs(sarea(o))-sum(abs(sarea(h)) for h in hs) for o,hs in polys)
    print(k,bg,'verts',len(v),'tris',len(t)//3,'loops',len(l),'area tri/poly %.3f/%.3f'%(tri_area,poly_area))
    res[k]=dict(bg=bg,v=v,t=t,l=l)
d=res

# --- C# emission ---
def area(pts): return sum(pts[i-1][0]*pts[i][1]-pts[i][0]*pts[i-1][1] for i in range(len(pts)))/2
out=[]
out.append('''// <auto-generated>
// Generated by Tools/Generate-AwsIconGeometry.py from the official AWS Architecture Service Icons (64 px, 80x80 viewBox)
// in aws-icons/Architecture-Service-Icons_07312026. Do not edit by hand; regenerate instead.
// Geometry: the white service symbol as triangulated outline polygons (even-odd fill resolved),
// coordinates normalised to a unit tile centred on 0 with +y up.
// </auto-generated>
namespace GuateGeeks.AwsVr
{
    public static partial class AwsIconGeometry
    {''')
for k,ic in d.items():
    # Quantise first (as emitted), then orient: clockwise as seen from -z (x right, y up) => negative signed area.
    v=[(round((x-40)/80,4),round((40-y)/80,4)) for x,y in ic['v']];src=ic['t'];t=[]
    for i in range(0,len(src),3):
        a,b,c=v[src[i]],v[src[i+1]],v[src[i+2]]
        cr=(b[0]-a[0])*(c[1]-a[1])-(c[0]-a[0])*(b[1]-a[1])
        if abs(cr)<1e-10: continue  # zero-area sliver after quantisation
        t+= [src[i],src[i+1],src[i+2]] if cr<0 else [src[i],src[i+2],src[i+1]]
    # loop direction so that filled material is on the left (outer CCW, holes CW)
    loops=[];
    # determine outer/hole: a loop is a hole if its midpoint probe lies inside an earlier outer loop of the same polygon;
    # the converter emitted each polygon as outer followed by its holes, outer loops have the largest |area| among following holes.
    i=0;L=ic['l']
    while i<len(L):
        s,n=L[i];loops.append((s,n,1 if area(v[s:s+n])>0 else -1));j=i+1
        # holes follow until next loop whose bbox is not inside this outer
        ox=[p[0] for p in v[s:s+n]];oy=[p[1] for p in v[s:s+n]]
        while j<len(L):
            hs,hn=L[j];hx=[p[0] for p in v[hs:hs+hn]];hy=[p[1] for p in v[hs:hs+hn]]
            if min(hx)>=min(ox) and max(hx)<=max(ox) and min(hy)>=min(oy) and max(hy)<=max(oy) and abs(area(v[hs:hs+hn]))<abs(area(v[s:s+n])):
                loops.append((hs,hn,1 if area(v[hs:hs+hn])<0 else -1));j+=1
            else: break
        i=j
    bg=ic['bg'].lstrip('#')
    fl=','.join('%.4ff,%.4ff'%(x,y) for x,y in v)
    out.append('        static readonly float[] %sPoints={%s};'%(k,fl))
    out.append('        static readonly int[] %sTriangles={%s};'%(k,','.join(map(str,t))))
    out.append('        static readonly int[] %sLoops={%s};'%(k,','.join('%d,%d,%d'%l for l in loops)))
    out.append('        const string %sBackground="#%s";'%(k,bg))
out.append('''        static Shape For(ServiceKind kind)
        {
            switch (kind)
            {''')
for k in d: out.append('                case ServiceKind.%s: return new Shape(%sPoints, %sTriangles, %sLoops, %sBackground);'%(k,k,k,k,k))
out.append('''                default: return null;
            }
        }
    }
}''')
open(os.path.join(ROOT,'Assets','GuateGeeks','Runtime','AwsIconGeometry.Data.cs'),'w',newline='\n').write('\n'.join(out)+'\n')
print('wrote Assets/GuateGeeks/Runtime/AwsIconGeometry.Data.cs')
