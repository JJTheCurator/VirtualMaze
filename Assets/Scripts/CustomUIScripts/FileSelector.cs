using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class FileSelector : InputField {
    public FileBrowser fb;
    public Button browseBtn;

    public string defaultPath;

    public OnPathSelectedEvent OnPathSelected = new OnPathSelectedEvent();

    protected override void Awake() {
        base.Awake();

        ResolveReferences();

        if (browseBtn != null) {
            browseBtn.onClick.AddListener(OnBrowseButtonClicked);
        }
        else {
            Debug.LogError("FileSelector could not find its browse button.", this);
        }

        onEndEdit.AddListener(OnEndEditPath);
        defaultPath = Application.dataPath;
    }

    private void OnBrowseButtonClicked() {
        ResolveReferences();

        if (fb == null) {
            Debug.LogError("FileSelector could not find a FileBrowser in the scene.", this);
            return;
        }

        fb.OnFileBrowserExit += OnBrowserExit;
        fb.TryShow(text, defaultPath);
    }

    private void ResolveReferences() {
        if (browseBtn == null) {
            browseBtn = GetComponentInChildren<Button>(true);
        }

        if (fb == null) {
            fb = FindObjectOfType<FileBrowser>();
        }
    }

    private void OnEndEditPath(string text) {
        OnPathSelected.Invoke(text);
    }

    private void OnBrowserExit(string path) {
        fb.OnFileBrowserExit -= OnBrowserExit;

        if (!string.IsNullOrEmpty(path)) {
            text = path;
            OnPathSelected.Invoke(path);
        }
    }

    protected override void OnDestroy() {
        if (browseBtn != null) {
            browseBtn.onClick.RemoveListener(OnBrowseButtonClicked);
        }

        onEndEdit.RemoveListener(OnEndEditPath);

        if (fb != null) {
            fb.OnFileBrowserExit -= OnBrowserExit;
        }

        base.OnDestroy();
    }

    [Serializable]
    public class OnPathSelectedEvent : UnityEvent<string> { }
}
