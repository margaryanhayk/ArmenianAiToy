using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ArmenianAiToy.Application.Services;

public class ChildService : IChildService
{
    private readonly DbContext _db;

    public ChildService(DbContext db)
    {
        _db = db;
    }

    public async Task<Child> CreateChildAsync(Guid deviceId, string name, Gender gender, int? birthYear)
    {
        var child = new Child
        {
            Id = Guid.NewGuid(),
            Name = name,
            Gender = gender,
            BirthYear = birthYear,
            DeviceId = deviceId
        };

        _db.Set<Child>().Add(child);
        await _db.SaveChangesAsync();
        return child;
    }

    public async Task<Child?> GetChildAsync(Guid childId)
    {
        return await _db.Set<Child>().FindAsync(childId);
    }

    public async Task<Child?> GetDefaultChildForDeviceAsync(Guid deviceId)
    {
        return await _db.Set<Child>()
            .Where(c => c.DeviceId == deviceId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Child>> GetChildrenByDeviceAsync(Guid deviceId)
    {
        return await _db.Set<Child>()
            .Where(c => c.DeviceId == deviceId)
            .ToListAsync();
    }

    public string BuildChildContext(Child child)
    {
        var age = child.GetAge();
        var genderWord = child.Gender == Gender.Boy ? "boy" : "girl";
        var genderArmenian = child.Gender == Gender.Boy ? "he/him" : "she/her";

        var context = $"\nCHILD PROFILE — use this to personalize your responses:\n";
        context += $"- Name: {child.Name}\n";
        context += $"- Gender: {genderWord} (use {genderArmenian} pronouns and gender-appropriate Armenian grammar)\n";

        if (age.HasValue)
        {
            context += $"- Age: {age} years old\n";
            context += $"- Adjust vocabulary complexity for a {age}-year-old\n";
        }

        // Name usage is deliberately BOUNDED. Areg is a warm play leader,
        // not a personal companion (MODES.md), so the child's name is a
        // light personal touch — never a tool for emotional attachment.
        context += "- Use the child's name naturally and warmly, but SPARINGLY"
            + " — not in every reply. Over-using it sounds robotic and"
            + " over-familiar; a light touch now and then is enough.\n";
        context += "- Use the name as given, in natural spoken Armenian direct"
            + " address. Do NOT invent nicknames, shorten it, or change its"
            + " form.\n";
        context += "- Do NOT pair the name with possessive or emotional"
            + " endearments (no \"my dear\", \"my special one\", \"I am always"
            + " with you\"). Keep it a friendly play-leader touch, never a"
            + " companion-attachment cue.\n";

        return context;
    }
}
