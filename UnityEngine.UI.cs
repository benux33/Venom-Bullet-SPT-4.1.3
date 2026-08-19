using UnityEngine;
namespace UnityEngine.UI
{
    public class Graphic : Behaviour
    {
        public bool raycastTarget { get; set; }
    }
    public class Image : Graphic
    {
        public Sprite sprite { get; set; }
        public bool preserveAspect { get; set; }
        public Color color { get; set; }
    }
    public class LayoutElement : Behaviour
    {
        public float minWidth { get; set; }
        public float minHeight { get; set; }
        public float preferredWidth { get; set; }
        public float preferredHeight { get; set; }
    }
}
namespace UnityEngine.EventSystems
{
    public class PointerEventData { }
}
