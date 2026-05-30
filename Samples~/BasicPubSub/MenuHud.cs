using UnityEngine;
using UnityEngine.UI;

namespace Tutan.Messages.Samples.BasicPubSub
{
    public sealed class MenuHud : MonoBehaviour
    {
        [SerializeField] Text _gameOver;
        [SerializeField] Button _button;

        private void OnEnable()
        {
            // Hide the game-over banner whenever the menu (re)appears — including the very
            // first show and between games. The composition root calls SetFinalScore after
            // re-activating us when a game ends.
            _gameOver.gameObject.SetActive(false);
            _button.onClick.AddListener(OnStartClicked);
        }

        private void OnDisable()
        {
            _button.onClick.RemoveListener(OnStartClicked);
        }

        public void SetFinalScore(int score)
        {
            _gameOver.text = $"Game Over\n\nFinal Score: {-score}";
            _gameOver.gameObject.SetActive(true);
        }

        private void OnStartClicked()
        {
            CommandBus.Publish(new StartGame());
        }
    }
}