using UnityEngine;
using UnityEngine.XR.Hands;

namespace GuateGeeks.AwsVr
{
    public sealed partial class LabRig
    {
        RectTransform palmMenu;
        float palmDwell, palmGrace;
        readonly Collider[] touchHits = new Collider[24];
        void BuildPalmMenu()
        {
            palmMenu=LabVisuals.Panel(transform,"Palm quick menu",Vector3.zero,new Vector2(440,300),movable:false);
            palmMenu.localScale=Vector3.one*.0008f;
            LabVisuals.Text(palmMenu,"PALMA / ACCIONES",new Vector2(0,112),new Vector2(410,42),22,LabVisuals.Cyan,TextAnchor.MiddleCenter);
            LabVisuals.Button(palmMenu,"Menús",new Vector2(-105,43),new Vector2(198,60),ResetMenus);
            LabVisuals.Button(palmMenu,"Guía",new Vector2(105,43),new Vector2(198,60),()=>lab.OpenGuidedDemo());
            LabVisuals.Button(palmMenu,"Inspección",new Vector2(-105,-38),new Vector2(198,60),lab.OpenSlots);
            LabVisuals.Button(palmMenu,"Ajustes",new Vector2(105,-38),new Vector2(198,60),()=>lab.OpenQuickSettings());
            LabVisuals.Text(palmMenu,"Toca con la otra mano",new Vector2(0,-111),new Vector2(410,40),19,LabVisuals.Muted,TextAnchor.MiddleCenter);
            palmMenu.gameObject.SetActive(false);
        }
        void UpdatePalm(XRHand hand, bool leftHand)
        {
            if(!leftHand) return;
            bool valid=hand.isTracked && hand.GetJoint(XRHandJointID.Palm).TryGetPose(out _);
            if(!valid || lab.Busy || lab.EditingText || lab.ConfiguringConnection || left.Held || left.HeldMenu) { HidePalm(); return; }
            hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var pose);
            Vector3 p=headOffset.TransformPoint(pose.position);
            Vector3 normal=headOffset.rotation*pose.rotation*Vector3.down;
            Vector3 toward=(cam.transform.position-p).normalized;
            bool facing=Vector3.Dot(normal,toward)>.65f;
            palmDwell=facing?palmDwell+Time.unscaledDeltaTime:0;
            if(facing) palmGrace=Time.unscaledTime+.45f;
            if(palmDwell>.35f) palmMenu.gameObject.SetActive(true);
            if(Time.unscaledTime>palmGrace) palmMenu.gameObject.SetActive(false);
            if(palmMenu.gameObject.activeSelf) {
                Vector3 destination=p+Vector3.up*.16f;
                palmMenu.position=Vector3.Lerp(palmMenu.position,destination,1-Mathf.Exp(-16*Time.unscaledDeltaTime));
                palmMenu.rotation=Quaternion.LookRotation(palmMenu.position-cam.transform.position,Vector3.up);
            } else palmMenu.position=p+Vector3.up*.16f;
        }
        void HidePalm() { palmDwell=0; if(palmMenu) palmMenu.gameObject.SetActive(false); }
        LabTarget TouchTarget(Vector3 tip, bool isLeft, out float distance)
        {
            distance=float.MaxValue; LabTarget best=null;
            int count=Physics.OverlapSphereNonAlloc(tip,.055f,touchHits,~0,QueryTriggerInteraction.Collide);
            for(int i=0;i<count;i++) {
                var target=touchHits[i].GetComponent<LabTarget>();
                if(!target || !target.Surface || target.Menu || !target.Available || !lab.CanInteract(target)) continue;
                if(isLeft && target.transform.IsChildOf(palmMenu)) continue;
                var rect=target.transform as RectTransform; if(!rect) continue;
                Vector3 local=rect.InverseTransformPoint(tip);
                float front=-local.z*Mathf.Abs(rect.lossyScale.z);
                if(!rect.rect.Contains(new Vector2(local.x,local.y)) || front<-.025f || front>.055f) continue;
                if(Mathf.Abs(front)<Mathf.Abs(distance)) { distance=front; best=target; }
            }
            return best;
        }
    }
}
