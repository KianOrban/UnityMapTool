using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MapToolWindow : EditorWindow
{
    //Hotkeys
    private readonly KeyCode _showSettingsKey = KeyCode.Space;
    private readonly KeyCode _toggleLockStateKey = KeyCode.L;
    private readonly KeyCode _togglePrecisionModeKey = KeyCode.LeftAlt;
    private readonly KeyCode _createNewAnchorKey = KeyCode.N;
    private readonly KeyCode _deleteAllAnchorsKey = KeyCode.Delete;
    private readonly KeyCode _snapCameraToObjKey = KeyCode.F;
    private readonly KeyCode _toggleOrthograpicViewKey = KeyCode.O;


    //Camera Attributes
    private Camera camera = null;
    private int cameraViewAngle = 90;
    private float cameraFOV = 90;
    private bool cameraOrthograpgic;
    private float orthographicCameraDistance = 50;

    //Rendering
    private Texture2D overlayMapTexture;
    private float textureScaling = 1f;
    private float overlayTransparency = 0.5f;
    private RenderTexture renderTexture;


    //Cache
    private GameObject FocusObject;
    private Texture2D lockIcon;
    private Texture2D precisionIcon;
    private GameObject anchorPrefab;
    private Rect renderLocationRect;

    //Input
    private bool isDragging = false;
    private Vector2 lastMousePosition;
    private float scrollSpeed = 10f;
    private float dragSpeed = 5f;
    private readonly float userPrecisionFactor = 0.05f; //only change this value to modify precision mode
    private float internalPrecsion = 1;
    private Vector3 hitPos;

    //UI Alignment 13 equals the amount of lines in Settings
    private float currentVerticalOffset;
    private readonly float mainSettingsVerticalOffset = 280;
    private readonly float additionalSettingsVerticalOffset = 230;
    private readonly float extraOrthographicSettingOffset = 35;

    //internal states
    private bool showSettings = false;
    private bool showAdditionalSettings = false;
    private bool renderUpdate = false; //re-render only when camera or window changed
    private bool lockedInput = false;

    //TODO: Display Gizmos Icons -> probably doesnt work with render textures that dont use the main cam

    MapToolWindow mapWindow;

    [MenuItem("Tools/MapTool")]
    public static void ShowWindow()
    {
        GetWindow<MapToolWindow>("MapTool");
    }

    private void OnEnable()
    {
        //Load Icons and Prefabs
        lockIcon = Resources.Load<Texture2D>("MapTool_lockedIcon");
        precisionIcon = Resources.Load<Texture2D>("MapTool_precisionIcon");
        anchorPrefab = Resources.Load<GameObject>("Prefabs/MapCamAnchor");

        ObjectChangeEvents.changesPublished += ChangedScene;

        mapWindow = GetWindow<MapToolWindow>(); //reference to current Window, used for icons

        //Check Scene for existing cam
        GameObject camObj = GameObject.FindGameObjectWithTag("MapToolCam");

        if (camObj != null)
        {
            if (camObj.TryGetComponent<Camera>(out Camera toolCam))
            {
                camera = toolCam;
            }
        }
        else
        {
            //if no cam in scene load prefab
            camObj = Resources.Load<GameObject>("Prefabs/MapToolCam");
            camera = Instantiate(camObj).GetComponent<Camera>();
            camera.transform.position = SceneView.lastActiveSceneView.camera.transform.position;
        }

        if (camObj != null)
        {
            //apply camera settings
            camera.transform.rotation = Quaternion.Euler(cameraViewAngle, 0, 0);
            cameraOrthograpgic = camera.orthographic;
            cameraFOV = Camera.VerticalToHorizontalFieldOfView(camera.fieldOfView, camera.aspect);
        }
        else
        {
            Debug.LogWarning("No MapToolCam found in scene" + SceneManager.GetActiveScene().name + "; Could not instantiate Prefab");
        }
    }

    //Main Loop
    private void OnGUI()
    {
        if (EditorApplication.isPlaying) return;

        Repaint();

        DisplaySettings();

        if (!CameraCheck()) return;

        RenderCamView();

        HandleUserInput();
    }

    //Update Toolcam when SceneView updates
    private void ChangedScene(ref ObjectChangeEventStream stream)
    {
        renderUpdate = true;
    }

    private void DisplaySettings()
    {
        //Toggle Settings visibility
        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == _showSettingsKey)
        {
            showSettings = !showSettings;
            e.Use();
        }

        showSettings = EditorGUILayout.Foldout(showSettings, $"Open Settings (Press {_showSettingsKey})", EditorStyles.boldFont);
        if (showSettings)
        {
            EditorGUILayout.BeginVertical("box");

            GUILayout.Label("Selected Camera:", EditorStyles.boldLabel);
            camera = EditorGUILayout.ObjectField("Camera", camera, typeof(Camera), true) as Camera;

            GUILayout.Label("Camera View Angle:", EditorStyles.boldLabel);
            cameraViewAngle = EditorGUILayout.IntSlider("View Angle", cameraViewAngle, -90, 90);

            if (cameraOrthograpgic) //conditional setting
            {
                GUILayout.Label("Orthograhpic Camera Distance:", EditorStyles.boldLabel);
                orthographicCameraDistance = EditorGUILayout.FloatField("Distance", Mathf.Max(orthographicCameraDistance, 1f));
            }

            GUILayout.Label("Select a map to display on top of the environment.", EditorStyles.boldLabel);
            overlayMapTexture = EditorGUILayout.ObjectField("Map Texture 2D", overlayMapTexture, typeof(Texture2D), true) as Texture2D;

            if (!overlayMapTexture.IsUnityNull()) //conditional setting
            {
                GUILayout.Label("Change transparency", EditorStyles.boldLabel);
                overlayTransparency = EditorGUILayout.Slider("Texture Transparency", overlayTransparency, 0f, 1f);
            }

            GUILayout.Label($"Please select a Player to teleport/Object to focus on. (Leftclick: Teleport, {_snapCameraToObjKey}: Focus)", EditorStyles.boldLabel);
            FocusObject = EditorGUILayout.ObjectField("Focus Object", FocusObject, typeof(GameObject), true) as GameObject;


            EditorGUILayout.EndVertical();

            showAdditionalSettings = EditorGUILayout.Foldout(showAdditionalSettings, "Show more settings", EditorStyles.boldFont);

            if (showAdditionalSettings)
            {
                EditorGUILayout.BeginVertical("box");

                if (!cameraOrthograpgic) ////conditional setting, doesnt apply to orthographic views
                {
                    GUILayout.Label("Camera Horizontal FOV:", EditorStyles.boldLabel);
                    cameraFOV = EditorGUILayout.Slider("FOV", cameraFOV, 0, 180);
                }

                GUILayout.Label($"Lock Input (Press {_toggleLockStateKey} to toggle):", EditorStyles.boldLabel);
                lockedInput = EditorGUILayout.Toggle(lockedInput);

                GUILayout.Label($"Toggle Orthographic View (Press {_toggleOrthograpicViewKey} to toggle):", EditorStyles.boldLabel);
                cameraOrthograpgic = EditorGUILayout.Toggle(cameraOrthograpgic);

                GUILayout.Label("Change texture scaling", EditorStyles.boldLabel);
                textureScaling = EditorGUILayout.Slider("Texture Scaling", textureScaling, 0f, 1f);

                GUILayout.Label("Adjust scrolling.", EditorStyles.boldLabel);
                scrollSpeed = EditorGUILayout.FloatField("Speed", scrollSpeed);

                GUILayout.Label("Adjust dragging.", EditorStyles.boldLabel);
                dragSpeed = EditorGUILayout.FloatField("Speed", dragSpeed);

                EditorGUILayout.EndVertical();
            }
        }
        else
        {
            //if Settings closed, revert to false so additional settings are minimized when opening again
            showAdditionalSettings = false;
        }
    }

    private void RenderCamView()
    {
        EditorGUILayout.BeginHorizontal("box");

        //update current UI offSet based on whether additional settings are displayed too
        if (showSettings)
        {
            currentVerticalOffset = cameraOrthograpgic == true ? mainSettingsVerticalOffset + extraOrthographicSettingOffset : mainSettingsVerticalOffset;

            if (showAdditionalSettings)
            {
                currentVerticalOffset += additionalSettingsVerticalOffset;
            }
        }

        // for width: if a overlayMapTexture is given, scale depending on its "width" and "width to height ratio" to preserve correct scaling
        renderLocationRect = new Rect(0,
            showSettings ? currentVerticalOffset : 2 * EditorGUIUtility.singleLineHeight,
            overlayMapTexture == null ? position.width : textureScaling * overlayMapTexture.width * (position.height / overlayMapTexture.height),
            textureScaling * position.height - (showSettings ? currentVerticalOffset : 2 * EditorGUIUtility.singleLineHeight));

        renderLocationRect.center = new Vector2(position.width / 2, position.height / 2 + (showSettings ? currentVerticalOffset / 2 : 0));

        if (renderLocationRect.height <= 1)
        {
            renderTexture.Release();
            EditorGUILayout.HelpBox("Texture scaling too small!", MessageType.Warning);
            EditorGUILayout.EndHorizontal();
            return;
        }

        if (renderTexture == null || renderTexture.width != (int)renderLocationRect.width || renderTexture.height != (int)renderLocationRect.height)
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
            }
            renderTexture = new RenderTexture((int)renderLocationRect.width, (int)renderLocationRect.height, 16);
            renderTexture.Create();
            renderUpdate = true;
        }

        //Check different camera settings for change
        if (cameraOrthograpgic)
        {
            if (!Mathf.Approximately(camera.orthographicSize, orthographicCameraDistance))
            {
                camera.orthographicSize = orthographicCameraDistance;
                renderUpdate = true;
            }
        }
        else if (!Mathf.Approximately(cameraFOV, Camera.VerticalToHorizontalFieldOfView(camera.fieldOfView, camera.aspect)))
        {
            camera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(cameraFOV, camera.aspect);
            renderUpdate = true;
        }

        if (!Mathf.Approximately(cameraViewAngle, camera.transform.eulerAngles.x))
        {
            camera.transform.rotation = Quaternion.Euler(cameraViewAngle, camera.transform.rotation.y, camera.transform.rotation.z);
            renderUpdate = true;
        }

        if (renderUpdate)
        {
            camera.targetTexture = renderTexture;
            camera.Render();
            renderUpdate = false;
        }

        GUI.DrawTexture(renderLocationRect, renderTexture, ScaleMode.ScaleToFit);

        if (overlayMapTexture != null)
        {
            Color originalColor = GUI.color;
            GUI.color = new Color(originalColor.r, originalColor.g, originalColor.b, overlayTransparency);
            GUI.DrawTexture(renderLocationRect, overlayMapTexture, ScaleMode.ScaleToFit);
            GUI.color = originalColor;
        }

        camera.targetTexture = null;
        EditorGUILayout.EndHorizontal();

    }

    private void HandleUserInput()
    {
        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;

        Rect windowRect = new Rect(0, 0, position.width, position.height);

        //react if cursor inside window
        if (windowRect.Contains(mousePos))
        {
            CreateAnchor(e);

            DeleteAllAnchors(e);

            if (ToggleOrthograpicView(e)) renderUpdate = true;

            //prevent input if cam is locked
            if (LockInput(e)) return;
        }

        //react if cursor over image only
        if (renderLocationRect.Contains(mousePos))
        {
            PrecisionMode(e);

            //x,z plane movement, and rotation
            if (DraggingBehaviour(e)) renderUpdate = true; //notify render to Update if cam got moved

            //control height
            if (ScrollingBehaviour(e)) renderUpdate = true;

            //Teleport Player with leftclick on terrain
            if (TeleportRaycast(e, mousePos)) renderUpdate = true;

            //Snap viewport to Focus Object location
            if (MoveCamToFocusObject(e, mousePos)) renderUpdate = true;

            //calling .Use() on these events would cause warnings
            if (e.type != EventType.Repaint && e.type != EventType.Layout)
            {
                e.Use();
            }
        }
    }
    private bool ToggleOrthograpicView(Event e)
    {
        if (e.type == EventType.KeyDown && e.keyCode == _toggleOrthograpicViewKey)
        {
            camera.orthographic = !camera.orthographic;
            cameraOrthograpgic = camera.orthographic;
            return true;
        }
        else if (cameraOrthograpgic != camera.orthographic)
        {
            camera.orthographic = cameraOrthograpgic;
            return true;
        }

        return false;
    }

    private bool LockInput(Event e)
    {
        if (e.type == EventType.KeyDown && e.keyCode == _toggleLockStateKey)
        {
            if (e.type != EventType.Repaint && e.type != EventType.Layout)
            {
                e.Use();
            }
            lockedInput = !lockedInput;

            //Display icon
            if (lockedInput)
            {
                mapWindow.titleContent.image = lockIcon;
            }
            else
            {
                mapWindow.titleContent.image = null;
            }
        }
        return lockedInput;
    }

    private void PrecisionMode(Event e)
    {
        //Left Alt is also used to increase precision when choosing value in inspector by dragging

        if (e.type == EventType.KeyDown && e.keyCode == _togglePrecisionModeKey)
        {
            mapWindow.titleContent.image = precisionIcon;
            internalPrecsion = userPrecisionFactor;
        }
        else if (e.type == EventType.KeyUp && e.keyCode == _togglePrecisionModeKey)
        {
            mapWindow.titleContent.image = null;
            internalPrecsion = 1;
        }
    }

    private void CreateAnchor(Event e)
    {
        if (e.type == EventType.KeyDown && e.keyCode == _createNewAnchorKey)
        {
            if (e.type != EventType.Repaint && e.type != EventType.Layout)
            {
                e.Use();
            }

            if (EditorUtility.DisplayDialog("Create Camera Anchor?", "Do you want to create a MapTool camera anchor at the current camera location?", "Create New Anchor", "Cancel"))
            {
                GameObject anchor = Instantiate(anchorPrefab);
                anchor.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
            }
        }
    }

    private void DeleteAllAnchors(Event e)
    {
        if (e.type == EventType.KeyDown && e.keyCode == _deleteAllAnchorsKey)
        {
            if (e.type != EventType.Repaint && e.type != EventType.Layout)
            {
                e.Use();
            }

            if (EditorUtility.DisplayDialog("Delete All Camera Anchors?", "Do you want to delete all MapTool camera anchors in this scene? This action cannot be undone!", "Delete All Anchors", "Cancel"))
            {
                GameObject[] anchors = GameObject.FindGameObjectsWithTag("MapCamAnchor");
                foreach (GameObject anchor in anchors)
                {
                    DestroyImmediate(anchor);
                }
            }
        }
    }

    //align cam view to a selected obj. use these anchors to ensure consitent camera setup between sessions
    private bool MoveCamToFocusObject(Event e, Vector2 mousePos)
    {
        if (e.type == EventType.KeyDown && e.keyCode == _snapCameraToObjKey)
        {
            if (FocusObject == null) return false;

            camera.transform.position = FocusObject.transform.position;
            Vector3 objRot = FocusObject.transform.rotation.eulerAngles;
            camera.transform.rotation = Quaternion.Euler(cameraViewAngle, objRot.y, objRot.z);

            return true;
        }
        return false;
    }

    private bool DraggingBehaviour(Event e)
    {
        if (e.type == EventType.MouseDown && e.button == 2 || e.type == EventType.MouseDown && e.button == 1) //Dragging with mouse wheel or right click
        {
            isDragging = true;
            lastMousePosition = e.mousePosition;
            return false;
        }

        if (e.type == EventType.MouseUp && e.button == 2 || e.type == EventType.MouseUp && e.button == 1) //Dragging with mouse wheel or right click
        {
            isDragging = false;
            return false;
        }

        if (e.type == EventType.MouseDrag && e.button == 2 && isDragging) //mouse wheel dragging
        {
            Vector3 delta = e.mousePosition - lastMousePosition;

            //scale delta.y to account for difference in area size to drag in, true value when in precision mode
            
            float deltaY = internalPrecsion == 1 ? delta.y * (renderTexture.width / renderTexture.height) : delta.y;

            //if at slanted angle, ignore cam rotation when moving in z-direction to prevent cam height change
            if (Mathf.Approximately(Mathf.Abs(cameraViewAngle), 90))
            {
                //Use self space so dragging to the right always move object to the right, undependend of rotation
                camera.transform.Translate(dragSpeed * internalPrecsion * Time.deltaTime * new Vector3(-delta.x, deltaY, 0), Space.Self);
            }
            else
            {
                float currentViewAngle = camera.transform.eulerAngles.x;
                camera.transform.rotation = Quaternion.Euler(Vector3.Scale(camera.transform.eulerAngles, new Vector3(0, 1, 1)));
                camera.transform.Translate(dragSpeed * internalPrecsion * Time.deltaTime * new Vector3(-delta.x, 0, deltaY), Space.Self);
                camera.transform.rotation = Quaternion.Euler(camera.transform.eulerAngles + new Vector3(currentViewAngle, 0, 0));
            }

            lastMousePosition = e.mousePosition;
            return true; //only return during continous part of draggig. returning in start or end results in stutters
        }

        if (e.type == EventType.MouseDrag && e.button == 1 && isDragging) //rotation dragging
        {
            Vector3 delta = e.mousePosition - lastMousePosition;
            camera.transform.RotateAround(camera.transform.position, Vector3.up, delta.x * internalPrecsion * dragSpeed * Time.deltaTime);
            lastMousePosition = e.mousePosition;
            return true;
        }

        return false;
    }

    private bool ScrollingBehaviour(Event e)
    {
        //camera height depending on scroll
        if (e.type == EventType.ScrollWheel)
        {
            float scrollAmount = e.delta.y * internalPrecsion * scrollSpeed * Time.deltaTime;
            Vector3 newCamPos = new Vector3(0, scrollAmount, 0);
            camera.transform.Translate(newCamPos, Space.World);
            return true;
        }
        return false;
    }

    private bool TeleportRaycast(Event e, Vector2 mousePos)
    {
        if (FocusObject == null) return false;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (showSettings)
            {
                Debug.LogWarning("Teleportation currently unsupported while settings are displayed!");
                return false;
            }

            //works for 1920x1080 
            float scaleX = 1.8f;
            float scaleY = 1.25f;

            //scaling based on basis resolution 1920x1080
            scaleX *= position.width / (float)1920;
            scaleY *= position.height / (float)1080;

            Vector2 correctedMousePos = new Vector2(mousePos.x, position.height - mousePos.y);
            correctedMousePos.x /= scaleX;
            correctedMousePos.y /= scaleY;

            Ray ray = camera.ScreenPointToRay(correctedMousePos);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                hitPos = hit.point;

                if (FocusObject != null && hit.transform.gameObject.name != FocusObject.name)
                {
                    if (FocusObject.CompareTag("Player"))
                    {
                        FocusObject.transform.position = hitPos;
                        //Move player up by an offset, prevent clipping in floor
                        FocusObject.transform.position += Vector3.up;
                        //print world pos if valid
                        if (hitPos != Vector3.zero)
                        {
                            //Debug.Log("Teleported Player: " + FocusObject.name + " to World Position:" + hitPos.ToString());
                        }
                        return true;
                    }
                    else
                    {
                        Debug.LogWarning(FocusObject.name + " doesn't share the 'Player' Tag, therefore teleportation is prohibited.");
                    }

                }
            }
        }
        return false;
    }


    private void OnDisable()
    {
        ObjectChangeEvents.changesPublished -= ChangedScene;

        //Cleanup
        if (renderTexture != null)
        {
            renderTexture.Release();
            renderTexture = null;
        }
    }

    private bool CameraCheck()
    {
        if (camera != null && camera.CompareTag("MainCamera"))
        {
            //prevent players from using main Cam
            camera = null;

            Debug.LogWarning("Main Camera is not supported!");

            return false;
        }

        if (camera == null)
        {
            if (!showSettings)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.HelpBox("No MapToolCam Found or inactive in Hierachy!", MessageType.Warning);
                EditorGUILayout.EndVertical();

            }
            return false;
        }

        return true;
    }
}


