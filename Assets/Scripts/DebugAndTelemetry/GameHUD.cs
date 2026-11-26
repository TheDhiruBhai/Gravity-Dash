using UnityEngine;

namespace GravityDrift
{
    public class GameHUD : MonoBehaviour
    {
        // Defer HUD rendering to GameUIManager if present
        private void Start()
        {
            if (GetComponent<GameUIManager>() == null && FindAnyObjectByType<GameUIManager>() == null)
            {
                gameObject.AddComponent<GameUIManager>();
            }
        }
    }
}
