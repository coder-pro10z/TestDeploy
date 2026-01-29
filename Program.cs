// Program.cs
// Minimal ASP.NET Core app for testing .NET + Azure DevOps CI/CD
// Target: .NET 8

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add basic services
builder.Services.AddHealthChecks();

var app = builder.Build();

// Enable static files (optional)
app.UseStaticFiles();

// Home Page (Single Page)
app.MapGet("/", () => Results.Content(@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>.NET + Azure DevOps Test App</title>
    <style>
        body {
            font-family: Arial, sans-serif;
            background: #0f172a;
            color: #e5e7eb;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
        }
        .card {
            background: #020617;
            padding: 30px 40px;
            border-radius: 12px;
            box-shadow: 0 0 20px rgba(0,0,0,0.6);
            max-width: 500px;
            text-align: center;
        }
        h1 {
            color: #38bdf8;
            margin-bottom: 10px;
        }
        p {
            line-height: 1.6;
        }
        .status {
            margin-top: 20px;
            padding: 10px;
            background: #020617;
            border: 1px solid #38bdf8;
            border-radius: 6px;
        }
        button {
            margin-top: 20px;
            padding: 10px 20px;
            border: none;
            border-radius: 6px;
            background: #38bdf8;
            color: #020617;
            font-weight: bold;
            cursor: pointer;
        }
        button:hover {
            background: #0ea5e9;
        }
    </style>
</head>
<body>
    <div class='card'>
        <h1>🚀 .NET + Azure DevOps</h1>
        <p>This is a single-page test app for validating:</p>
        <p>
            ✅ .NET Build<br>
            ✅ CI/CD Pipeline<br>
            ✅ Azure Deployment<br>
            ✅ Health Checks
        </p>

        <div class='status' id='status'>Checking API status...</div>

        <button onclick='checkHealth()'>Check Health</button>
    </div>

    <script>
        async function checkHealth() {
            try {
                const res = await fetch('/health');
                if (res.ok) {
                    document.getElementById('status').innerText = '✅ API is Healthy';
                } else {
                    document.getElementById('status').innerText = '⚠️ API Error';
                }
            } catch {
                document.getElementById('status').innerText = '❌ API Not Reachable';
            }
        }

        checkHealth();
    </script>
</body>
</html>
", "text/html"));

// Health Check Endpoint (For DevOps / Azure Monitoring)
app.MapHealthChecks("/health");

// Sample API Endpoint (Testing Deployments)
app.MapGet("/api/info", () => new
{
    App = ".NET DevOps Test",
    Version = "1.0.0",
    Environment = app.Environment.EnvironmentName,
    Time = DateTime.UtcNow
});

// Run App
app.Run();

/*
--------------------------------------------------
HOW TO USE (Azure DevOps Ready)
--------------------------------------------------

1. Create Project
   dotnet new web -n DevOpsTestApp
   Replace Program.cs with this file

2. Run Locally
   dotnet run
   Open: http://localhost:5000

3. Push to Git (Azure Repos/GitHub)
   git init
   git add .
   git commit -m "Initial DevOps test app"

4. Create Azure DevOps Pipeline
   Use: .NET Core template

5. Deploy
   - Azure App Service
   - Azure Web App
   - Container (Optional)

Endpoints:
   /        -> UI Page
   /health  -> Health Check
   /api/info-> Test API
--------------------------------------------------
*/
