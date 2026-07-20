using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    public sealed class CurrentUserView : MonoBehaviour
    {
        [SerializeField] private Text currentUserText;
        [SerializeField] private Button logoutButton;


        private void Awake()
        {
            if (currentUserText == null)
            {
                var userObject = GameObject.Find("CurrentUser");
                currentUserText = userObject != null ? userObject.GetComponent<Text>() : null;
            }

            if (logoutButton == null)
            {
                var logoutObject = GameObject.Find("LogoutButton");
                logoutButton = logoutObject != null ? logoutObject.GetComponent<Button>() : null;
            }

            if (currentUserText != null)
            {
                currentUserText.gameObject.SetActive(false);
            }

            if (logoutButton != null)
            {
                logoutButton.gameObject.SetActive(false);
            }
        }

        private void Start()
        {
        }

        private void OnDestroy()
        {
        }

        private void RefreshLocalUserText()
        {
        }

        private void Refresh()
        {
        }

        public void LogoutToLoginScene()
        {
        }
    }
}
