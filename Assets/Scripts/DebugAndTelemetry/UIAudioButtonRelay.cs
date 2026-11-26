using UnityEngine;
using UnityEngine.EventSystems;

namespace GravityDrift
{
    /// <summary>
    /// Lightweight UI pointer helper forwarding hover sounds cleanly to SoundManager.
    /// Kept in its own file so Unity can serialize it properly on UI Prefabs & Scene GameObjects.
    /// </summary>
    public class UIAudioButtonRelay : MonoBehaviour, IPointerEnterHandler
    {
        public void OnPointerEnter(PointerEventData eventData)
        {
            SoundManager.Instance?.PlayUIHover();
        }
    }
}
