using ArmenianAiToy.Application.DTOs;

namespace ArmenianAiToy.Application.Interfaces;

public interface IChatService
{
    Task<ChatResponse> GetResponseAsync(Guid deviceId, string userMessage, Guid? childId = null,
        Guid? storySessionId = null, string? selectedChoice = null);

    /// <summary>
    /// Hands-free autoplay step for an active library story. No child
    /// utterance: advances the device's active library session by one
    /// segment and returns that segment (or the ending/reflection turn
    /// when the story finishes). When the engine is off or no live
    /// session exists, returns a response with an empty
    /// <see cref="ChatResponse.Response"/> and
    /// <see cref="ChatResponse.LibraryAutoContinue"/> = false so the
    /// transport stops the autoplay loop. Never runs the legacy
    /// pipeline, ModeDetector, or GPT.
    /// </summary>
    Task<ChatResponse> ContinueLibraryStoryAsync(Guid deviceId, Guid? childId = null);
}
