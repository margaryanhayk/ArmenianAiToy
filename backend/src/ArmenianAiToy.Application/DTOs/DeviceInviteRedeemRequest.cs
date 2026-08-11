namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Request body for POST /api/parents/devices/redeem-invite. Deliberately
/// carries ONLY the code: the whole point of an invite is that the joining
/// parent does not need the toy's id, which the printed-code path
/// (<see cref="DeviceClaimRequest"/>) does require. The selector embedded in
/// the code is what finds the toy.
/// </summary>
public record DeviceInviteRedeemRequest(string Code);
