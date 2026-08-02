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

    /// <summary>
    /// Device-scoped child lookup: returns the child ONLY when it belongs to
    /// the calling device. A <c>childId</c> arriving on a chat request is
    /// client-supplied (the firmware sends it), so it must never be trusted on
    /// its own — an id belonging to another family would otherwise pull that
    /// child's name / age / gender into this device's system prompt and stamp
    /// the conversation with it. Same cross-device probe guard
    /// <see cref="DeviceService.IsModeEnabledForRequestAsync"/> already applies
    /// to the mode-flag path; this closes the child-context path to match.
    /// </summary>
    public async Task<Child?> GetChildForDeviceAsync(Guid childId, Guid deviceId)
    {
        return await _db.Set<Child>()
            .FirstOrDefaultAsync(c => c.Id == childId && c.DeviceId == deviceId);
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

        context += $"- Address the child by name sometimes to make conversation feel personal\n";

        return context;
    }
}
