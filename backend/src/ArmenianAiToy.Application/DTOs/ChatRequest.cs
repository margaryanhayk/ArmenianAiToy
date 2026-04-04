using System.ComponentModel;

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Chat message from device to AI.
/// </summary>
/// <param name="Message">The child's message text.</param>
/// <param name="ChildId">Optional child profile ID for personalization.</param>
public record ChatRequest(
    [property: DefaultValue("tell me a story")] string Message,
    Guid? ChildId = null);
