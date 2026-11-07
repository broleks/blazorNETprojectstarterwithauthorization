// Minimal helpers for the native <dialog> element used by ConfirmDialog.razor.
export function showModal(dialog, dotNetRef) {
    dialog.oncancel = () => dotNetRef.invokeMethodAsync("OnCancelled");
    dialog.showModal();
}

export function close(dialog) {
    if (dialog.open) {
        dialog.close();
    }
}
