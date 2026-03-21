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

    public async Task<Child> CreateChildAsync(Guid deviceId, string name, Gender gender, DateOnly? dateOfBirth)
    {
        var child = new Child
        {
            Id = Guid.NewGuid(),
            Name = name,
            Gender = gender,
            DateOfBirth = dateOfBirth,
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

        context += $"- Address the child by name sometimes to make conversation feel personal\n";

        return context;
    }
}
