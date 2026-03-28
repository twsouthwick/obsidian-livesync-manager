using System.ComponentModel.DataAnnotations;

namespace obsidian_sync_manager.Web;

public sealed class OidcGroupOptions
{
    [Required]
    public string Admins { get; set; } = "obsidian-admins";
    
    [Required]
    public string Users { get; set; } = "obsidian-users";
}
