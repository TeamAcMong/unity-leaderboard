using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>Các trạng thái ngoài danh sách: đang tải (hiện trễ để lần tải nhanh không nháy), lỗi + Retry, rỗng.</summary>
    [DisallowMultipleComponent]
    public sealed class LeaderboardStatusView : MonoBehaviour
    {
        [SerializeField] private GameObject loadingRoot;
        [SerializeField] private TMP_Text loadingText;
        [SerializeField] private GameObject errorRoot;
        [SerializeField] private TMP_Text errorText;
        [SerializeField] private Button retryButton;
        [SerializeField] private TMP_Text retryLabel;
        [SerializeField] private GameObject emptyRoot;
        [SerializeField] private TMP_Text emptyText;

        private bool _isInitialized;
        private bool _isLoading;
        private float _loadingElapsed;
        private float _loadingDelay;
        private float _dotsPerSecond = 3f;
        private string _loadingBaseText = string.Empty;
        private int _shownDots = -1;

        public event Action RetryClicked;

        public bool IsLoading => _isLoading;
        public bool IsShowingError => errorRoot && errorRoot.activeSelf;
        public bool IsShowingEmpty => emptyRoot && emptyRoot.activeSelf;

        public void ShowLoading(string baseText, float delay, float dotsPerSecond)
        {
            EnsureInitialized();
            HideAll();
            _isLoading = true;
            _loadingElapsed = 0f;
            _loadingDelay = Mathf.Max(0f, delay);
            _dotsPerSecond = dotsPerSecond;
            _loadingBaseText = baseText ?? string.Empty;
            _shownDots = -1;
            if (_loadingDelay <= 0f) RevealLoading();
        }

        public void ShowError(string message, string retryText)
        {
            EnsureInitialized();
            HideAll();
            if (errorText) errorText.text = message;
            if (retryLabel && retryText != null) retryLabel.text = retryText;
            if (errorRoot) errorRoot.SetActive(true);
        }

        public void ShowEmpty(string message)
        {
            EnsureInitialized();
            HideAll();
            if (emptyText) emptyText.text = message;
            if (emptyRoot) emptyRoot.SetActive(true);
        }

        public void HideAll()
        {
            _isLoading = false;
            SetActive(loadingRoot, false);
            SetActive(errorRoot, false);
            SetActive(emptyRoot, false);
        }

        private void Update()
        {
            if (!_isLoading) return;
            _loadingElapsed += Time.unscaledDeltaTime;
            if (_loadingElapsed < _loadingDelay) return;
            if (loadingRoot && !loadingRoot.activeSelf) RevealLoading();
            int dots = (int)((_loadingElapsed - _loadingDelay) * _dotsPerSecond) % 4;
            if (dots == _shownDots || loadingText == null) return;
            _shownDots = dots;
            loadingText.text = _loadingBaseText + new string('.', dots);
        }

        private void RevealLoading()
        {
            if (loadingText) loadingText.text = _loadingBaseText;
            SetActive(loadingRoot, true);
        }

        private void EnsureInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            if (retryButton) retryButton.onClick.AddListener(() => RetryClicked?.Invoke());
        }

        private static void SetActive(GameObject target, bool isActive)
        {
            if (target != null && target.activeSelf != isActive) target.SetActive(isActive);
        }
    }
}
