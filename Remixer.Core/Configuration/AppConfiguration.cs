namespace Remixer.Core.Configuration;

public static class AppConfiguration
{
    // AI Service Configuration
    public static string AIEndpoint { get; set; } = "";
    public static string AIKey { get; set; } = "";

    // Initialize with default values or configure here
    static AppConfiguration()
    {
        // Set your DigitalOcean AI endpoint here
        // Base URL only - the /api/v1/chat/completions path will be added automatically
        // Format: https://<agent-identifier>.ondigitalocean.app
        // Or: https://<agent-identifier>.agents.do-ai.run
        AIEndpoint = "https://imwqa7a4jl5zij4t2rx3tn6b.agents.do-ai.run";
        
        // Set your API key here (DigitalOcean Agent Access Key)
        AIKey = "DhQWWI8SrHmpRDRksz36OLYRlQTGvyJH";
        
    }
}

