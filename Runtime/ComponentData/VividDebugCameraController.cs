using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VividRP.Runtime
{
    [ExecuteInEditMode]
    public class VividDebugCameraController : MonoBehaviour
    {
        #region Configuration

        [Header("Sync Settings")] [SerializeField]
        private bool _sceneSync = true;

        [SerializeField] [Range(0.1f, 10f)] private float _lerpSpeed = 2.5f;
        [SerializeField] private bool _syncFOV = true;

        [Header("Movement Settings")] [SerializeField]
        private float _moveSpeed = 5f;

        [SerializeField] private float _rotationSpeed = 120f;

        #endregion

        #region Runtime

        private Transform _targetCamera;
        private Camera _localCamera;
        private bool _emergencyStop;
#if UNITY_EDITOR
        private double _lastEditorTime;
#endif

        #endregion

        #region Unity Events

        private void Awake()
        {
            _localCamera = GetComponent<Camera>();
#if UNITY_EDITOR
            if (!Application.isPlaying) InitEditorResources();
#endif
        }

#if UNITY_EDITOR
        private void OnEnable()
        {
            EditorApplication.update += EditorUpdate;
            SceneView.duringSceneGui += OnSceneGUI;
            InitEditorResources();
            _lastEditorTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
            SceneView.duringSceneGui -= OnSceneGUI;
            ReleaseEditorResources();
        }
#endif

        private void Update()
        {
            if (Application.isPlaying && !_emergencyStop)
            {
                HandleKeyboardInput();
                HandleMouseRotation();
            }
        }

        #endregion

        #region Core Logic

        private void HandleKeyboardInput()
        {
            Vector3 movement = new Vector3(
                Input.GetAxis("Horizontal"),
                0,
                Input.GetAxis("Vertical")
            ) * (_moveSpeed * Time.deltaTime);

            transform.Translate(movement, Space.Self);
        }

        private void HandleMouseRotation()
        {
            if (Input.GetMouseButton(1))
            {
                float x = Input.GetAxis("Mouse X") * _rotationSpeed * Time.deltaTime;
                float y = Input.GetAxis("Mouse Y") * _rotationSpeed * Time.deltaTime;
                transform.Rotate(-y, x, 0, Space.Self);
            }
        }

        #endregion

        #region Editor Sync

#if UNITY_EDITOR
        private void InitEditorResources()
        {
            if (SceneView.lastActiveSceneView != null &&
                SceneView.lastActiveSceneView.camera != null)
            {
                _targetCamera = SceneView.lastActiveSceneView.camera.transform;
            }
        }

        private void ReleaseEditorResources() => _targetCamera = null;

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_sceneSync || sceneView == null) return;
            if (sceneView.camera != null) _targetCamera = sceneView.camera.transform;
        }

        private void EditorUpdate()
        {
            if (Application.isPlaying || _emergencyStop) return;

            try
            {
                if (_targetCamera != null)
                {
                    SmoothSync();
                    if (_syncFOV) SyncFOV();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Camera Sync Error: {ex.Message}");
                _emergencyStop = true;
                EditorApplication.delayCall += () => _emergencyStop = false;
            }
        }

        private float GetEditorDeltaTime()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            float delta = (float)(currentTime - _lastEditorTime);
            _lastEditorTime = currentTime;
            return Mathf.Clamp(delta, 0.0001f, 0.1f);
        }

        private void SmoothSync()
        {
            float delta = GetEditorDeltaTime() * 0.95f;

            transform.position = Vector3.Lerp(
                transform.position,
                _targetCamera.position,
                _lerpSpeed * delta
            );

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                _targetCamera.rotation,
                _lerpSpeed * delta
            );
        }

        private void SyncFOV()
        {
            if (_localCamera != null &&
                _targetCamera.TryGetComponent<Camera>(out var targetCam))
            {
                _localCamera.fieldOfView = Mathf.Lerp(
                    _localCamera.fieldOfView,
                    targetCam.fieldOfView,
                    _lerpSpeed * GetEditorDeltaTime()
                );
            }
        }
#endif

        #endregion

        #region Public Methods

        public void ResetCamera()
        {
            _emergencyStop = false;
#if UNITY_EDITOR
            if (SceneView.lastActiveSceneView != null &&
                SceneView.lastActiveSceneView.camera != null)
            {
                _targetCamera = SceneView.lastActiveSceneView.camera.transform;
            }
#endif
        }

        #endregion
    }
}