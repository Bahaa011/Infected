using UnityEngine;

public interface IInteractionPromptSource
{
    bool TryGetInteractionPrompt(Transform viewer, out string prompt);
}
