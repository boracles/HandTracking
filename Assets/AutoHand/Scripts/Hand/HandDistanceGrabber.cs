using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using NaughtyAttributes;

namespace Autohand 
{
    [DefaultExecutionOrder(2)]
    [HelpURL("https://app.gitbook.com/s/5zKO0EvOjzUDeT2aiFk3/auto-hand/grabbable/distance-grabbing")]
    public class HandDistanceGrabber : MonoBehaviour {
        [Header("Hands")]
        [Tooltip("The primaryHand used to trigger pulling or flicking")]
        public Hand primaryHand;
        [Tooltip("This is important for catch assistance")]
        public Hand secondaryHand;

        [Header("Pointing Options")]
        public Transform forwardPointer;
        public bool useSmoothing = true;
        public float forwardSmoothingSpeed = 5f;
        public LineRenderer line;
        public Material[] lineMat;
        
        [Space]
        public float maxRange = 5;
        [Tooltip("Defaults to grabbable on start if none")]
        public LayerMask layers;
        [Space]
        public Material defaultTargetedMaterial;
        [Tooltip("The highlight material to use when pulling")]
        public Material defaultSelectedMaterial;

        [Header("Pull Options")]
        public bool useInstantPull = false;
        [Tooltip("If false will default to distance pull, set pullGrabDistance to 0 for instant pull on select")]
        public bool useFlickPull = false;


        [Tooltip("The magnitude of your hands angular velocity for \"flick\" to start")]
        [ShowIf("useFlickPull")]
        public float flickThreshold = 7f;


        [Tooltip("The amount you need to move your hand from the select position to trigger the grab")]
        [HideIf("useFlickPull")]
        public float pullGrabDistance = 0.1f;

        [Space]
        [Tooltip("If this is true the object will be grabbed when entering the radius")]
        public bool instantGrabAssist = true;
        [Tooltip("The radius around of thrown object")]
        public float catchAssistRadius = 0.2f;

        [AutoToggleHeader("Show Events")]
        public bool showEvents = true;

        [ShowIf("showEvents")]
        public UnityHandGrabEvent OnPull;
        [ShowIf("showEvents")]
        public UnityHandEvent StartPoint;
        [ShowIf("showEvents")]
        public UnityHandEvent StopPoint;
        [ShowIf("showEvents"), Tooltip("Targeting is started when object is highlighted")]
        public UnityHandGrabEvent StartTarget;
        [ShowIf("showEvents")]
        public UnityHandGrabEvent StopTarget;
        [ Tooltip("Selecting is started when grab is selected on highlight object")]
        [ShowIf("showEvents")]
        public UnityHandGrabEvent StartSelect;
        [ShowIf("showEvents")]
        public UnityHandGrabEvent StopSelect;
        
        List<CatchAssistData> catchAssisted;

        DistanceGrabbable targetingDistanceGrabbable;
        DistanceGrabbable selectingDistanceGrabbable;

        float catchAssistSeconds = 3f;
        bool pointing;
        bool pulling;
        Vector3 startPullPosition;
        RaycastHit hit;
        Quaternion lastRotation;
        private RaycastHit selectionHit;
        float selectedEstimatedRadius;
        float startLookAssist;
        bool lastInstantPull;

        GameObject _hitPoint;
        Coroutine catchAssistRoutine;
        private DistanceGrabbable catchAsistGrabbable;
        private CatchAssistData catchAssistData;
        private GameObject attachedObject;
        private Vector3 initialObjectOffset; // 손과 객체 간 초기 간격
        private bool isGripping = false; 
        private float lastGripTime = 0f; // Grip 동작의 마지막 활성화 시간
        private float gripReleaseDelay = 0.3f; // Grip 상태 해제를 지연시키는 시간 (초 단위)
        private bool isAttached = false; // 물건이 현재 Attach 상태인지 여부

        
        Vector3 currentSmoothForward;

        private bool isPointerBlue = false; // 포인터가 하늘색 상태인지 추적
        private GameObject selectedObject;
        private Quaternion initialRotationOffset;
        GameObject hitPoint 
        {
            get {
                if(!gameObject.activeInHierarchy)
                    return null;

                if(_hitPoint == null) {
                    _hitPoint = new GameObject();
                    _hitPoint.name = "Distance Hit Point";
                    return _hitPoint;
                }

                return _hitPoint;
            }
        }

        void Start() 
        {
            catchAssisted = new List<CatchAssistData>();
            if(layers == 0)
                layers = LayerMask.GetMask(Hand.grabbableLayerNameDefault);

            if(useInstantPull)
                SetInstantPull();
        }

        private void OnEnable() {
            primaryHand.OnTriggerGrab += TryCatchAssist;
            if(secondaryHand != null)
                secondaryHand.OnTriggerGrab += TryCatchAssist;
            primaryHand.OnBeforeGrabbed += (hand, grabbable) => { StopPointing(); CancelSelect(); };

        }

        private void OnDisable() {
            primaryHand.OnTriggerGrab -= TryCatchAssist;
            if(secondaryHand != null)
                secondaryHand.OnTriggerGrab -= TryCatchAssist;
            primaryHand.OnBeforeGrabbed -= (hand, grabbable) => { StopPointing(); CancelSelect(); };

            if(catchAssistRoutine != null) {
                StopCoroutine(catchAssistRoutine);
                catchAssistRoutine = null;
                catchAsistGrabbable.grabbable.OnGrabEvent -= (hand, grabbable) => { if(catchAssisted.Contains(catchAssistData)) catchAssisted.Remove(catchAssistData); };
                catchAsistGrabbable.OnPullCanceled -= (hand, grabbable) => { if(catchAssisted.Contains(catchAssistData)) catchAssisted.Remove(catchAssistData); };
            }
        }
        
        void Update() 
        {
            CheckDistanceGrabbable();
            CheckPointerState();

            if(lastInstantPull != useInstantPull) 
            {
                if(useInstantPull) {
                    useFlickPull = false;
                    pullGrabDistance = 0;
                }
                lastInstantPull = useInstantPull;
            }
        }

        private void FixedUpdate()
        {
            UpdateObjectPosition();
        }

        
        private void OnDestroy() {
            Destroy(hitPoint);
        }
        public void SetInstantPull() {
            useInstantPull = true;
        }

        public void SetPull(float distance) {
            useInstantPull = false;
            useFlickPull = false;
            pullGrabDistance = distance;
        }

        public void SetFlickPull(float threshold) {
            useInstantPull = false;
            useFlickPull = true;
            flickThreshold = threshold;
        }


        void CheckDistanceGrabbable() 
        {
            if(useSmoothing) 
            {
                var currentAngleDistance = Vector3.Angle(currentSmoothForward, forwardPointer.forward);
                currentSmoothForward = Vector3.RotateTowards(currentSmoothForward, forwardPointer.forward, Time.deltaTime * forwardSmoothingSpeed + Time.deltaTime * forwardSmoothingSpeed * currentAngleDistance, 1000f);
                currentSmoothForward.Normalize();
            }
            else
                currentSmoothForward = forwardPointer.forward;

            if(!pulling && pointing && primaryHand.holdingObj == null) 
            {
                bool didHit = Physics.SphereCast(forwardPointer.position, 0.03f, currentSmoothForward, out hit, maxRange, layers);
                
                DistanceGrabbable hitGrabbable;
                GrabbableChild hitGrabbableChild;
                
                if(didHit) 
                {
                    if (hit.transform.gameObject.layer == 10) 
                    {
                        if (line != null) 
                        {
                            var material= line.material;
                            material = lineMat[0];
                            line.material = material;
                        }
                    }
     
                    if(hit.transform.CanGetComponent(out hitGrabbable)) 
                    {
                        if(targetingDistanceGrabbable == null || hitGrabbable.GetInstanceID() != targetingDistanceGrabbable.GetInstanceID())
                        {
                            StartTargeting(hitGrabbable);
                        }
                    }
                    else if(hit.transform.CanGetComponent(out hitGrabbableChild)) 
                    {
                        if(hitGrabbableChild.grabParent.transform.CanGetComponent(out hitGrabbable)) {
                            if(targetingDistanceGrabbable == null || hitGrabbable.GetInstanceID() != targetingDistanceGrabbable.GetInstanceID())
                            {
                                StartTargeting(hitGrabbable);
                            }
                        }
                    }
                    else if(targetingDistanceGrabbable != null && hit.transform.gameObject.GetInstanceID() != targetingDistanceGrabbable.gameObject.GetInstanceID())
                    {
                        StopTargeting();
                    }
                }
                else 
                {
                    if (line != null) 
                    {
                        var material= line.material;
                        material = lineMat[1]; // 빨간색
                        line.material = material;
                    }
                    StopTargeting();
                }

                if(line != null) {
                    if(didHit) {
                        line.positionCount = 2;
                        line.SetPositions(new Vector3[] { forwardPointer.position, hit.point });
                    }
                    else {
                        line.positionCount = 2;
                        line.SetPositions(new Vector3[] { forwardPointer.position, forwardPointer.position + currentSmoothForward * maxRange });
                    }
                }
            }
            else if(pulling && primaryHand.holdingObj == null) {
                if(useFlickPull) {
                    TryFlickPull();
                }
                else {
                    TryDistancePull();
                }
            }
            else if(targetingDistanceGrabbable != null) {
                StopTargeting();
            }
        }

        void CheckPointerState()
        {
            bool didHit = Physics.SphereCast(forwardPointer.position, 0.03f, forwardPointer.forward, out RaycastHit hit, maxRange, layers);

            if (didHit && hit.transform.gameObject.layer == 10)
            {
                isPointerBlue = true;
                selectedObject = hit.collider.gameObject;
                if (line != null)
                {
                    line.material = lineMat[0];
                }
            }
            else
            {
                isPointerBlue = false;
                selectedObject = null;
                if (line != null)
                {
                    line.material = lineMat[1];
                }
            }

            if (isGripping)
            {
                isPointerBlue = true; // Grip 상태 유지
            }
        }
        public void OnGripStart()
        {
            if (isAttached) // 이미 물건이 Attach된 상태라면 호출하지 않음
                return;

            if (isPointerBlue && selectedObject != null && !isGripping)
            {
                isGripping = true;
                lastGripTime = Time.time; // Grip 시작 시간 기록
                initialObjectOffset = selectedObject.transform.position - primaryHand.transform.position;
                initialRotationOffset = Quaternion.Inverse(primaryHand.transform.rotation) * selectedObject.transform.rotation;
                AttachObject(selectedObject);

                // Attach 상태로 전환
                isAttached = true;
                Debug.Log($"Grip 시작: {selectedObject.name}");
            }
        }
        private void AttachObject(GameObject obj)
        {
            if (attachedObject != null || obj == null)
                return;

            attachedObject = obj;
            var rigidbody = attachedObject.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.isKinematic = true; // 물리 효과 비활성화
                rigidbody.useGravity = false; // 중력 비활성화
                rigidbody.linearVelocity = Vector3.zero; // 기존 속도 초기화
                rigidbody.angularVelocity = Vector3.zero; // 기존 회전 초기화
            }

            Debug.Log($"객체 {obj.name}가 Grip 상태에서 포인터에 붙었습니다.");
        }
        public void OnGripStop()
        {
            if (isGripping && Time.time - lastGripTime >= gripReleaseDelay)
            {
                isGripping = false;
                DetachObject();
                Debug.Log("Grip 해제: 물건이 포인터에서 분리되었습니다.");
            }
            else
            {
                Debug.Log($"Grip 해제 대기 중: {Time.time - lastGripTime:F2}초 경과");
            }
        }

        private void DetachObject()
        {
            if (attachedObject == null)
                return;

            var rigidbody = attachedObject.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.isKinematic = false; // 물리 효과 활성화
                rigidbody.useGravity = true; // 중력 활성화
            }

            attachedObject.transform.SetParent(null);
            attachedObject = null;

            // Attach 상태 초기화
            isAttached = false;
            Debug.Log("물건이 포인터에서 분리되었습니다.");
        }
        
        private void UpdateObjectPosition()
        {
            if (isGripping && attachedObject != null)
            {
                // 객체 위치 및 회전 동기화
                attachedObject.transform.position = Vector3.Lerp(
                    attachedObject.transform.position, 
                    primaryHand.transform.position + initialObjectOffset, 
                    Time.fixedDeltaTime * 10f // 부드럽게 이동
                );
                attachedObject.transform.rotation = Quaternion.Slerp(
                    attachedObject.transform.rotation, 
                    primaryHand.transform.rotation * initialRotationOffset, 
                    Time.fixedDeltaTime * 10f // 부드럽게 회전
                );
            }
        }
        
        public virtual void StartPointing() 
        {
            pointing = true;
            currentSmoothForward = forwardPointer.forward;
            StartPoint?.Invoke(primaryHand);

            if (attachedObject != null && !isGripping)
            {
                DetachObject();
                Debug.Log("포인터 활성화, 물건을 놓습니다.");
            }
        }

        public virtual void StopPointing() {
            pointing = false;
            if(line != null) {
                line.positionCount = 0;
                line.SetPositions(new Vector3[0]);
            }
            StopPoint?.Invoke(primaryHand);
            StopTargeting();
        }
        

        public virtual void StartTargeting(DistanceGrabbable target) 
        {
            if(target.enabled && primaryHand.CanGrab(target.grabbable)) {
                if(targetingDistanceGrabbable != null)
                    StopTargeting();
                targetingDistanceGrabbable = target;
                targetingDistanceGrabbable?.grabbable.Highlight(primaryHand, GetTargetedMaterial(targetingDistanceGrabbable));
                targetingDistanceGrabbable?.StartTargeting?.Invoke(primaryHand, target.grabbable);
                StartTarget?.Invoke(primaryHand, target.grabbable);
            }
        }

        public virtual void StopTargeting() {
            targetingDistanceGrabbable?.grabbable.Unhighlight(primaryHand, GetTargetedMaterial(targetingDistanceGrabbable));
            targetingDistanceGrabbable?.StopTargeting?.Invoke(primaryHand, targetingDistanceGrabbable.grabbable);
            if(targetingDistanceGrabbable != null)
                StopTarget?.Invoke(primaryHand, targetingDistanceGrabbable.grabbable);
            else if(selectingDistanceGrabbable != null)
                StopTarget?.Invoke(primaryHand, selectingDistanceGrabbable.grabbable);
            targetingDistanceGrabbable = null;
        }

        public virtual void SelectTarget() {
            if(targetingDistanceGrabbable != null) {
                pulling = true;
                startPullPosition = primaryHand.transform.localPosition;
                lastRotation = transform.rotation;
                selectionHit = hit;
                if(catchAssistRoutine == null) {
                    hitPoint.transform.position = selectionHit.point;
                    hitPoint.transform.parent = selectionHit.transform;
                }
                selectingDistanceGrabbable = targetingDistanceGrabbable;
                selectedEstimatedRadius = Vector3.Distance(hitPoint.transform.position, selectingDistanceGrabbable.grabbable.body.transform.position);
                selectingDistanceGrabbable.grabbable.Unhighlight(primaryHand, GetTargetedMaterial(selectingDistanceGrabbable));
                selectingDistanceGrabbable.grabbable.Highlight(primaryHand, GetSelectedMaterial(selectingDistanceGrabbable));
                selectingDistanceGrabbable?.StartSelecting?.Invoke(primaryHand, selectingDistanceGrabbable.grabbable);
                targetingDistanceGrabbable?.StopTargeting?.Invoke(primaryHand, selectingDistanceGrabbable.grabbable);
                targetingDistanceGrabbable = null;
                StartSelect?.Invoke(primaryHand, selectingDistanceGrabbable.grabbable);
                StopPointing();
            }
        }

        public virtual void CancelSelect() {
            StopTargeting();
            pulling = false;
            selectingDistanceGrabbable?.grabbable.Unhighlight(primaryHand, GetSelectedMaterial(selectingDistanceGrabbable));
            selectingDistanceGrabbable?.StopSelecting?.Invoke(primaryHand, selectingDistanceGrabbable.grabbable);
            if(selectingDistanceGrabbable != null)
                StopSelect?.Invoke(primaryHand, selectingDistanceGrabbable.grabbable);
            selectingDistanceGrabbable = null;
        }

        public virtual void ActivatePull() {
            if(selectingDistanceGrabbable) {
                OnPull?.Invoke(primaryHand, selectingDistanceGrabbable.grabbable);
                selectingDistanceGrabbable.OnPull?.Invoke(primaryHand, selectingDistanceGrabbable.grabbable);
                if(selectingDistanceGrabbable.instantPull) {
                    selectingDistanceGrabbable.grabbable.body.linearVelocity = Vector3.zero;
                    selectingDistanceGrabbable.grabbable.body.angularVelocity = Vector3.zero;
                    selectionHit.point = hitPoint.transform.position;
                    if (selectingDistanceGrabbable.grabbable.placePoint != null)
                        selectingDistanceGrabbable.grabbable.placePoint.Remove();
                    primaryHand.Grab(selectionHit, selectingDistanceGrabbable.grabbable);
                    CancelSelect();
                    selectingDistanceGrabbable?.CancelTarget();
                }
                else if(selectingDistanceGrabbable.grabType == DistanceGrabType.Velocity) {
                    catchAssistRoutine = StartCoroutine(StartCatchAssist(selectingDistanceGrabbable, selectedEstimatedRadius));
                    catchAsistGrabbable = selectingDistanceGrabbable;
                    if (selectingDistanceGrabbable.grabbable.placePoint != null)
                    {
                        
                        selectingDistanceGrabbable.grabbable.placePoint.Remove();
                    }
                    selectingDistanceGrabbable.SetTarget(primaryHand.palmTransform);
                }
                else if(selectingDistanceGrabbable.grabType == DistanceGrabType.Linear) {
                    selectingDistanceGrabbable.grabbable.body.linearVelocity = Vector3.zero;
                    selectingDistanceGrabbable.grabbable.body.angularVelocity = Vector3.zero;
                    selectionHit.point = hitPoint.transform.position;
                    if (selectingDistanceGrabbable.grabbable.placePoint != null)
                        selectingDistanceGrabbable.grabbable.placePoint.Remove();
                    primaryHand.Grab(selectionHit, selectingDistanceGrabbable.grabbable, GrabType.GrabbableToHand);
                    CancelSelect();
                    selectingDistanceGrabbable?.CancelTarget();

                }

                    CancelSelect();
            }
        }


        void TryDistancePull() {
            if(Vector3.Distance(startPullPosition, primaryHand.transform.localPosition) > pullGrabDistance) {
                ActivatePull();
            }
        }

        void TryFlickPull() {
            Quaternion deltaRotation = transform.rotation * Quaternion.Inverse(lastRotation);
            lastRotation = transform.rotation;
            var getAngle = 0f;
            Vector3 getAxis = Vector3.zero;
            deltaRotation.ToAngleAxis(out getAngle, out getAxis);
            getAngle *= Mathf.Deg2Rad;
            float speed = (getAxis * getAngle * (1f / Time.deltaTime)).magnitude;

            if(speed > flickThreshold || useInstantPull) {
                if(selectingDistanceGrabbable) {
                    ActivatePull();
                }
            }
        }




        Material GetSelectedMaterial(DistanceGrabbable grabbable) {
            if(grabbable.ignoreHighlights)
                return null;
            return grabbable.selectedMaterial != null ? grabbable.selectedMaterial : defaultSelectedMaterial;
        }
        Material GetTargetedMaterial(DistanceGrabbable grabbable) {
            if(grabbable.ignoreHighlights)
                return null;
            return grabbable.selectedMaterial != null ? grabbable.targetedMaterial : defaultTargetedMaterial;
        }

        void TryCatchAssist(Hand hand, Grabbable grab) {
            for(int i = 0; i < catchAssisted.Count; i++) {
                var distance = Vector3.Distance(hand.palmTransform.position + hand.palmTransform.forward * catchAssistRadius, catchAssisted[i].grab.transform.position) - catchAssisted[i].estimatedRadius;
                if(distance < catchAssistRadius) {
                    Ray ray = new Ray(hand.palmTransform.position, hitPoint.transform.position - hand.palmTransform.position);
                    if(Physics.SphereCast(ray, 0.03f, out var catchHit, catchAssistRadius * 2, LayerMask.GetMask(Hand.grabbableLayerNameDefault, Hand.grabbingLayerName))) {
                        if(catchHit.transform.gameObject == catchAssisted[i].grab.gameObject) {
                            catchAssisted[i].grab.body.linearVelocity = Vector3.zero;
                            catchAssisted[i].grab.body.angularVelocity = Vector3.zero;
                            hand.Grab(catchHit, catchAssisted[i].grab);
                            CancelSelect();
                        }
                    }
                }
            }
        }


        IEnumerator StartCatchAssist(DistanceGrabbable grab, float estimatedRadius) {
            catchAssistData = new CatchAssistData(grab.grabbable, catchAssistRadius);
            catchAssisted.Add(catchAssistData);
            grab.grabbable.OnGrabEvent += (hand, grabbable) => { if(catchAssisted.Contains(catchAssistData)) catchAssisted.Remove(catchAssistData); };
            grab.OnPullCanceled += (hand, grabbable) => { if(catchAssisted.Contains(catchAssistData)) catchAssisted.Remove(catchAssistData); };

            if(instantGrabAssist) {
                bool cancelInstantGrab = false;
                var time = 0f;
                primaryHand.OnTriggerRelease += (hand, grabbable) => { cancelInstantGrab = true; };

                while(time < catchAssistSeconds && !cancelInstantGrab) {
                    time += Time.fixedDeltaTime;

                    if(TryCatch(primaryHand))
                        break;

                    bool TryCatch(Hand hand) {
                        var distance = Vector3.Distance(hand.palmTransform.position + hand.palmTransform.forward * catchAssistRadius, grab.transform.position) - estimatedRadius;
                        if(distance < catchAssistRadius) {
                            Ray ray = new Ray(hand.palmTransform.position, hitPoint.transform.position - hand.palmTransform.position);
                            var hits = Physics.SphereCastAll(ray, 0.03f, catchAssistRadius * 2, LayerMask.GetMask(Hand.grabbableLayerNameDefault, Hand.grabbingLayerName));
                            for(int i = 0; i < hits.Length; i++) {
                                if(hits[i].transform.gameObject == grab.gameObject) {
                                    grab.grabbable.body.linearVelocity = Vector3.zero;
                                    grab.grabbable.body.angularVelocity = Vector3.zero;
                                    hand.Grab(hits[i], grab.grabbable);
                                    grab.CancelTarget();
                                    CancelSelect();
                                    return true;
                                }
                            }
                        }
                        return false;
                    }

                    yield return new WaitForEndOfFrame();
                }

                primaryHand.OnTriggerRelease -= (hand, grabbable) => { cancelInstantGrab = true; };

            }

            else
                yield return new WaitForSeconds(catchAssistSeconds);

            grab.grabbable.OnGrabEvent -= (hand, grabbable) => { if(catchAssisted.Contains(catchAssistData)) catchAssisted.Remove(catchAssistData); };
            grab.OnPullCanceled -= (hand, grabbable) => { if(catchAssisted.Contains(catchAssistData)) catchAssisted.Remove(catchAssistData); };
            if(catchAssisted.Contains(catchAssistData))
                catchAssisted.Remove(catchAssistData);

            catchAssistRoutine = null;
        }

        private void OnDrawGizmosSelected() {
            if(primaryHand)
                Gizmos.DrawWireSphere(primaryHand.palmTransform.position + primaryHand.palmTransform.forward * catchAssistRadius * 4 / 5f + primaryHand.palmTransform.up * catchAssistRadius * 1 / 4f, catchAssistRadius);
        }
    }
    
    struct CatchAssistData {
        public Grabbable grab;
        public float estimatedRadius;

        public CatchAssistData(Grabbable grab, float estimatedRadius) {
            this.grab = grab;
            this.estimatedRadius = estimatedRadius;
        }
    }
}
