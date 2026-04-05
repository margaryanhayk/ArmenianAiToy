using System.ComponentModel;

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Chat message from device to AI.
/// </summary>
/// <param name="Message">The child's message text.</param>
/// <param name="ChildId">Optional child profile ID for personalization.</param>
/// <param name="StorySessionId">Story session to continue (returned as StorySessionId in previous response).</param>
/// <param name="SelectedChoice">"A" or "B" — explicit choice selection from the previous story turn.</param>
public record ChatRequest(
    [property: DefaultValue("tell me a story")] string Message,
    Guid? ChildId = null,
    Guid? StorySessionId = null,
    string? SelectedChoice = null);
