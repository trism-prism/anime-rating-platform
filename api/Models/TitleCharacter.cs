namespace api.Models;

public class TitleCharacter
{
    public int Id { get; set; }
    public int TitleId { get; set; }
    public Title Title { get; set; } = null!;
    public int CharacterId { get; set; }
    public Character Character { get; set; } = null!;
    public string Role { get; set; } = "";
    public int? VoiceActorId { get; set; }
    public VoiceActor? VoiceActor { get; set; }
}
