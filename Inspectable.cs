using UnityEngine;

/// <summary>
/// Marca un objeto como "examinable". Ponlo en el objeto (o en su raíz) junto con un Collider.
/// Los datos que escribas aquí se muestran en el panel del ObjectInspector.
/// </summary>
public class Inspectable : MonoBehaviour
{
    [Tooltip("Nombre que se muestra. Si lo dejas vacío se usa el nombre del GameObject.")]
    [SerializeField] private string displayName = "";

    [TextArea(2, 8)]
    [SerializeField] private string description = "";

    [Tooltip("Líneas de información extra, por ejemplo: \"Material: madera\" o \"Peso: 2 kg\".")]
    [SerializeField] private string[] details;

    [Tooltip("Muestra las dimensiones calculadas automáticamente a partir de los renderers.")]
    [SerializeField] private bool showDimensions = true;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public string Description => description;
    public string[] Details => details;
    public bool ShowDimensions => showDimensions;
}