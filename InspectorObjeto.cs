using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Inspector de objetos en primera persona (Unity 6, Input System nuevo).
///
/// Mira un objeto con el componente Inspectable y pulsa E: el objeto se acerca a la cámara,
/// puedes rotarlo con el ratón, acercarlo/alejarlo con la rueda y ver su información.
/// Pulsa E otra vez para devolverlo a su sitio.
///
/// Ponlo en el mismo objeto que FirstPersonController (el Player).
/// La interfaz usa OnGUI, así que no necesitas crear ningún Canvas.
/// </summary>
[DisallowMultipleComponent]
public class ObjectInspector : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Cámara del jugador. Si se deja vacío se usa Camera.main.")]
    [SerializeField] private Transform cameraTransform;
    [Tooltip("Se desactiva mientras inspeccionas. Si se deja vacío se busca en este objeto.")]
    [SerializeField] private FirstPersonController playerController;

    [Header("Detección")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactMask = ~0;

    [Header("Inspección")]
    [Tooltip("Distancia inicial del objeto frente a la cámara.")]
    [SerializeField] private float inspectDistance = 0.8f;
    [SerializeField] private float minDistance = 0.4f;
    [SerializeField] private float maxDistance = 1.6f;
    [Tooltip("Ajusta el objeto para que su lado más grande mida esto (en metros) al inspeccionarlo.")]
    [SerializeField] private bool fitToView = true;
    [SerializeField] private float targetSize = 0.45f;
    [SerializeField] private float moveSmoothing = 12f;
    [SerializeField] private float rotationSpeed = 0.35f;
    [SerializeField] private float zoomSpeed = 0.0008f;

    // Estado
    private Inspectable hovered;
    private Inspectable current;
    private bool returning;

    private Transform pivot;
    private float currentDistance;
    private float fitScale = 1f;
    private Vector3 inspectedSize;

    // Datos originales para restaurar el objeto exactamente como estaba
    private Transform originalParent;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Vector3 originalLocalScale;
    private Vector3 originalCenter;
    private Quaternion originalWorldRotation;

    private readonly List<Collider> disabledColliders = new List<Collider>();
    private Rigidbody currentBody;
    private bool bodyWasKinematic;

    // Estilos de la interfaz
    private GUIStyle titleStyle, bodyStyle, hintStyle;

    private void Awake()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (playerController == null)
            playerController = GetComponent<FirstPersonController>();

        if (cameraTransform == null)
            Debug.LogError("[ObjectInspector] No hay cámara asignada.", this);
    }

    private void OnDisable()
    {
        // Evita dejar un objeto "atascado" a mitad de inspección si se desactiva el script.
        if (current != null)
            FinishInspect();
    }

    private void Update()
    {
        if (cameraTransform == null)
            return;

        if (current == null)
        {
            UpdateHover();
            if (hovered != null && InteractPressed())
                BeginInspect(hovered);
        }
        else if (returning)
        {
            UpdateReturn();
        }
        else
        {
            UpdateInspect();
        }
    }

    // ---------- Entrada ----------

    private static bool InteractPressed()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame) return true;

        Gamepad pad = Gamepad.current;
        return pad != null && pad.buttonWest.wasPressedThisFrame;
    }

    // ---------- Detección ----------

    private void UpdateHover()
    {
        hovered = null;

        if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit,
                interactRange, interactMask, QueryTriggerInteraction.Ignore))
        {
            hovered = hit.collider.GetComponentInParent<Inspectable>();
        }
    }

    // ---------- Inspección ----------

    private void BeginInspect(Inspectable target)
    {
        current = target;
        returning = false;
        hovered = null;

        Transform tr = current.transform;

        // Guardar el estado original
        originalParent = tr.parent;
        originalLocalPosition = tr.localPosition;
        originalLocalRotation = tr.localRotation;
        originalLocalScale = tr.localScale;
        originalWorldRotation = tr.rotation;

        Bounds bounds = CalculateBounds(tr);
        originalCenter = bounds.center;
        inspectedSize = bounds.size;

        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        fitScale = (fitToView && maxDimension > 0.0001f) ? targetSize / maxDimension : 1f;

        // Pivote en el centro del objeto, para que rote sobre sí mismo aunque su origen esté descentrado.
        pivot = new GameObject("InspectPivot").transform;
        pivot.position = bounds.center;
        pivot.rotation = tr.rotation;
        tr.SetParent(pivot, true);

        // Evitar que empuje al jugador o que la física lo mueva mientras se inspecciona.
        disabledColliders.Clear();
        foreach (Collider c in tr.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled) continue;
            c.enabled = false;
            disabledColliders.Add(c);
        }

        currentBody = tr.GetComponent<Rigidbody>();
        if (currentBody != null)
        {
            bodyWasKinematic = currentBody.isKinematic;
            if (!bodyWasKinematic)
            {
                currentBody.linearVelocity = Vector3.zero;
                currentBody.angularVelocity = Vector3.zero;
            }
            currentBody.isKinematic = true;
        }

        currentDistance = Mathf.Clamp(inspectDistance, minDistance, maxDistance);

        if (playerController != null)
            playerController.enabled = false;
    }

    private void UpdateInspect()
    {
        if (InteractPressed())
        {
            returning = true;
            return;
        }

        // Rotar con el ratón
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * rotationSpeed;
                pivot.Rotate(cameraTransform.up, -delta.x, Space.World);
                pivot.Rotate(cameraTransform.right, delta.y, Space.World);
            }

            // Acercar / alejar con la rueda
            float scroll = mouse.scroll.ReadValue().y;
            currentDistance = Mathf.Clamp(currentDistance - scroll * zoomSpeed, minDistance, maxDistance);
        }

        // Rotar con el stick derecho si hay mando
        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 stick = pad.rightStick.ReadValue() * (200f * Time.deltaTime);
            pivot.Rotate(cameraTransform.up, -stick.x, Space.World);
            pivot.Rotate(cameraTransform.right, stick.y, Space.World);
        }

        // Mover suavemente hacia el punto frente a la cámara
        float t = 1f - Mathf.Exp(-moveSmoothing * Time.deltaTime);
        Vector3 targetPosition = cameraTransform.position + cameraTransform.forward * currentDistance;
        pivot.position = Vector3.Lerp(pivot.position, targetPosition, t);
        pivot.localScale = Vector3.Lerp(pivot.localScale, Vector3.one * fitScale, t);
    }

    private void UpdateReturn()
    {
        float t = 1f - Mathf.Exp(-moveSmoothing * Time.deltaTime);
        pivot.position = Vector3.Lerp(pivot.position, originalCenter, t);
        pivot.rotation = Quaternion.Slerp(pivot.rotation, originalWorldRotation, t);
        pivot.localScale = Vector3.Lerp(pivot.localScale, Vector3.one, t);

        bool closeEnough =
            (pivot.position - originalCenter).sqrMagnitude < 0.0001f &&
            Quaternion.Angle(pivot.rotation, originalWorldRotation) < 1f;

        if (closeEnough)
            FinishInspect();
    }

    private void FinishInspect()
    {
        if (current != null)
        {
            Transform tr = current.transform;

            // Restaurar padre y transform locales exactamente como estaban
            tr.SetParent(originalParent, false);
            tr.localPosition = originalLocalPosition;
            tr.localRotation = originalLocalRotation;
            tr.localScale = originalLocalScale;

            foreach (Collider c in disabledColliders)
                if (c != null) c.enabled = true;

            if (currentBody != null)
            {
                currentBody.isKinematic = bodyWasKinematic;
                if (!bodyWasKinematic) currentBody.WakeUp();
            }
        }

        disabledColliders.Clear();
        currentBody = null;

        if (pivot != null)
            Destroy(pivot.gameObject);
        pivot = null;

        current = null;
        returning = false;

        if (playerController != null)
            playerController.enabled = true;
    }

    private static Bounds CalculateBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            Bounds b = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
                b.Encapsulate(colliders[i].bounds);
            return b;
        }

        return new Bounds(root.position, Vector3.one * 0.1f);
    }

    // ---------- Interfaz (OnGUI) ----------

    private void OnGUI()
    {
        if (cameraTransform == null)
            return;

        EnsureStyles();

        if (current == null)
        {
            // Mira central
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (hovered != null)
                DrawCenteredBox($"[E] Examinar:  {hovered.DisplayName}", Screen.height * 0.8f);
        }
        else if (!returning)
        {
            DrawInfoPanel();
            DrawCenteredBox("Ratón: rotar     Rueda: acercar / alejar     E: cerrar", Screen.height * 0.92f);
        }
    }

    private void DrawInfoPanel()
    {
        float margin = 24f;
        float padding = 18f;
        float width = Mathf.Clamp(Screen.width * 0.3f, 260f, 520f);

        Rect panel = new Rect(Screen.width - width - margin, margin, width, Screen.height - margin * 2f);
        DrawBackground(panel);

        GUILayout.BeginArea(new Rect(panel.x + padding, panel.y + padding,
                                     panel.width - padding * 2f, panel.height - padding * 2f));

        GUILayout.Label(current.DisplayName, titleStyle);

        if (!string.IsNullOrWhiteSpace(current.Description))
        {
            GUILayout.Space(8f);
            GUILayout.Label(current.Description, bodyStyle);
        }

        bool hasDetails = current.Details != null && current.Details.Length > 0;
        if (hasDetails || current.ShowDimensions)
            GUILayout.Space(14f);

        if (hasDetails)
        {
            foreach (string line in current.Details)
                if (!string.IsNullOrWhiteSpace(line))
                    GUILayout.Label("•  " + line, bodyStyle);
        }

        if (current.ShowDimensions)
            GUILayout.Label("•  Dimensiones: " + FormatDimensions(inspectedSize), bodyStyle);

        GUILayout.EndArea();
    }

    private void DrawCenteredBox(string text, float y)
    {
        GUIContent content = new GUIContent(text);
        Vector2 size = hintStyle.CalcSize(content);
        float padX = 18f;
        float padY = 8f;

        Rect box = new Rect((Screen.width - size.x) * 0.5f - padX, y, size.x + padX * 2f, size.y + padY * 2f);
        DrawBackground(box);
        GUI.Label(new Rect(box.x + padX, box.y + padY, size.x, size.y), content, hintStyle);
    }

    private static void DrawBackground(Rect rect)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void EnsureStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            bodyStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            hintStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, alignment = TextAnchor.MiddleCenter };

            titleStyle.normal.textColor = Color.white;
            bodyStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            hintStyle.normal.textColor = Color.white;
        }

        // El tamaño de letra se adapta a la resolución.
        int baseSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.022f));
        titleStyle.fontSize = Mathf.RoundToInt(baseSize * 1.5f);
        bodyStyle.fontSize = baseSize;
        hintStyle.fontSize = baseSize;
    }

    private static string FormatDimensions(Vector3 s)
    {
        float max = Mathf.Max(s.x, s.y, s.z);
        if (max >= 1f)
            return $"{s.x:F2} × {s.y:F2} × {s.z:F2} m";

        return $"{s.x * 100f:F1} × {s.y * 100f:F1} × {s.z * 100f:F1} cm";
    }
}