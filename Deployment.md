# CI/CD Pipeline Guide: GitHub Actions to Azure Web Apps

## 🎯 Overview
This document explains how to deploy .NET applications to Azure Web Apps using GitHub Actions CI/CD pipelines. We'll cover everything from basic deployment to advanced blue-green deployment strategies.

---

## 🚀 Quick Start: Your First Pipeline (V1)

### **1. Basic Test Deployment Pipeline**
Create this file in your repository: `.github/workflows/deploy.yml`

```yaml
# File: .github/workflows/deploy.yml
name: Deploy to Azure

# WHEN: This runs when you manually trigger it from GitHub Actions tab
on:
  workflow_dispatch:

# WHAT: The deployment job
jobs:
  deploy:
    runs-on: ubuntu-latest  # WHERE: Runs on GitHub's Ubuntu virtual machine
    
    steps:
      # Step 1: Get your code
      - name: 📥 Checkout Code
        uses: actions/checkout@v4  # Downloads your repository code
      
      # Step 2: Prepare .NET environment
      - name: ⚙️ Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.x'  # Matches your .NET version
      
      # Step 3: Create deployable package
      - name: 📦 Publish Application
        run: dotnet publish -c Release -o ./publish
        # 📝 Creates optimized files in ./publish folder
        # Release = Production-optimized, smaller, faster
      
      # Step 4: Deploy to Azure
      - name: 🚀 Deploy to Azure
        uses: azure/webapps-deploy@v2
        with:
          app-name: ${{ secrets.AZURE_WEBAPP_NAME }}  # Your Azure app name
          publish-profile: ${{ secrets.AZURE_PUBLISH_PROFILE }}  # Azure credentials
          package: ./publish  # What to deploy
```

### **2. How to Run Your First Deployment**
```bash
# 1. Set up GitHub Secrets (ONE TIME SETUP)
# Go to: Your Repo → Settings → Secrets → Actions → New Repository Secret

# Add these two secrets:
# - AZURE_WEBAPP_NAME: "your-app-name" (from Azure)
# - AZURE_PUBLISH_PROFILE: (Paste entire XML from Azure Portal)

# 2. Run the pipeline
# - Commit the YAML file above
# - Go to GitHub → Actions tab
# - Click "Deploy to Azure"
# - Click "Run workflow"
```

### **3. What Happens Behind the Scenes**
```
Your Computer → GitHub → Azure
     ↓             ↓        ↓
   Push    →  Triggers →  Deploys
   Code       Pipeline     to Web App

Detailed Flow:
1. GitHub creates Ubuntu virtual machine
2. Machine clones your repository
3. .NET SDK installed (if needed)
4. Runs: dotnet publish (creates production files)
5. Authenticates to Azure using publish profile
6. Uploads files to your Web App
7. Azure restarts app with new files
```

---

## 🔧 Modifying an Existing Pipeline

### **Common Changes & How to Make Them**

#### **A. Change Trigger from Manual to Automatic**
```yaml
# BEFORE (Manual only):
on:
  workflow_dispatch:

# AFTER (Auto on push to main):
on:
  push:
    branches: [main]  # ✅ Auto-runs when code is pushed to main branch
  workflow_dispatch:  # ✅ Still keep manual option
```

#### **B. Add a Build Step Before Deploy**
```yaml
steps:
  # Existing checkout step...
  
  - name: 🏗️ Build Solution
    run: dotnet build --configuration Release
    # 🚨 ALWAYS build before publish to catch errors early
    # --configuration Release = Production-optimized build
  
  - name: ✅ Run Tests
    run: dotnet test
    # 🛡️ Safety check: Don't deploy if tests fail
    # Can add --verbosity normal for more details
  
  # Then continue with publish and deploy...
```

#### **C. Add Performance Optimizations**
```yaml
# Add caching for faster builds
- name: 💾 Cache NuGet Packages
  uses: actions/cache@v3
  with:
    path: ~/.nuget/packages  # Where packages are stored
    key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
    # 🔑 Key changes when .csproj changes = fresh cache
    # Same .csproj = reuse cache = faster builds
```

---

## 🌉 Advanced: Multi-Stage Deployment with Slots (V2)

### **1. Understanding Azure Deployment Slots**
```
Azure Web App with Slots:
┌─────────────────────────────────┐
│  PRODUCTION (blue)              │ ← Users go here
│  https://myapp.azurewebsites.net│
├─────────────────────────────────┤
│  STAGING (green)                │ ← We deploy here first
│  https://myapp-staging.azure...│
├─────────────────────────────────┤
│  DEVELOPMENT                    │ ← Optional: for testing
│  https://myapp-dev.azurewebs...│
└─────────────────────────────────┘

Blue-Green Deployment Flow:
1. Deploy new version to STAGING (green)
2. Test it thoroughly
3. SWAP staging ↔ production (milliseconds)
4. If problems: SWAP back immediately
```

### **2. Multi-Environment Pipeline**
```yaml
name: Multi-Stage Deployment

# Trigger on main branch pushes
on:
  push:
    branches: [main]

# Environment variables (keep configuration in one place)
env:
  AZURE_WEBAPP_NAME: 'myapp'
  DOTNET_VERSION: '8.x'

jobs:
  # JOB 1: BUILD (Runs on every push)
  build:
    name: 🏗️ Build and Test
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
      
      - name: 💾 Cache Dependencies
        uses: actions/cache@v3
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
      
      - name: 🔨 Build
        run: dotnet build --configuration Release
      
      - name: 🧪 Test
        run: dotnet test
      
      - name: 📦 Package
        run: dotnet publish -c Release -o ./publish
      
      - name: 💿 Save Artifact
        uses: actions/upload-artifact@v4
        with:
          name: app-package
          path: ./publish

  # JOB 2: DEPLOY TO STAGING (Only if build succeeds)
  deploy-staging:
    name: 🚀 Deploy to Staging
    runs-on: ubuntu-latest
    needs: build  # ⚠️ REQUIRES: build job to succeed first
    environment: staging  # 🌐 GitHub Environment for protection
    
    steps:
      - name: 📥 Download Artifact
        uses: actions/download-artifact@v4
        with:
          name: app-package
      
      - name: 🔐 Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}  # More secure than publish profile
      
      - name: 🎯 Deploy to Staging Slot
        uses: azure/webapps-deploy@v2
        with:
          app-name: ${{ env.AZURE_WEBAPP_NAME }}
          slot-name: 'staging'  # 🟢 Deploy to GREEN slot
          package: .

  # JOB 3: DEPLOY TO PRODUCTION (Manual approval + swap)
  deploy-production:
    name: ✅ Deploy to Production
    runs-on: ubuntu-latest
    needs: deploy-staging
    environment: production  # 🔐 Requires manual approval in GitHub
    
    steps:
      - name: 🔄 Swap Staging to Production
        uses: azure/webapps-deploy@v2
        with:
          app-name: ${{ env.AZURE_WEBAPP_NAME }}
          slot-name: 'staging'
          action: swap  # 🔄 Magic! Swaps staging ↔ production
          # 💡 This takes <1 second, zero downtime
```

### **3. How to Set Up Environments in GitHub**
```bash
# In your GitHub repository:
# 1. Go to Settings → Environments
# 2. Click "New environment"
# 3. Name: "staging"
# 4. Name: "production"
# 5. In "production" environment, enable:
#    - "Required reviewers" (someone must approve)
#    - "Wait timer" (optional delay before deployment)
```

### **4. Setting Up Azure for Multi-Slot Deployment**
```bash
# Prerequisite: Install Azure CLI (az)

# 1. Create resource group (once)
az group create --name MyResourceGroup --location eastus

# 2. Create App Service plan (server pricing)
az appservice plan create --name MyPlan --resource-group MyResourceGroup --sku B1

# 3. Create Web App
az webapp create --name myapp --plan MyPlan --resource-group MyResourceGroup

# 4. Create staging slot
az webapp deployment slot create --name myapp --resource-group MyResourceGroup --slot staging

# 5. Create Service Principal for GitHub (more secure)
az ad sp create-for-rbac --name github-actions-myapp \
  --role contributor \
  --scopes /subscriptions/YOUR-SUB-ID/resourceGroups/MyResourceGroup \
  --sdk-auth
# ⚠️ Save the JSON output as GitHub secret: AZURE_CREDENTIALS
```

---

## 🚨 Common Problems & Solutions

### **Problem 1: "No web project found"**
**Solution**: Check your .csproj file exists and is in the root
```yaml
# If project is in subfolder:
- name: Publish
  run: dotnet publish ./src/MyProject/MyProject.csproj -c Release -o ./publish
```

### **Problem 2: Slow builds every time**
**Solution**: Add caching
```yaml
- name: Cache
  uses: actions/cache@v3
  with:
    path: |
      ~/.nuget/packages
      **/bin
      **/obj
    key: ${{ runner.os }}-build-${{ hashFiles('**/*.csproj') }}
```

### **Problem 3: Deployment works but app doesn't start**
**Solution**: Check Azure logs
```bash
# After deployment fails:
az webapp log tail --name myapp --resource-group MyResourceGroup

# Common issues:
# - Missing connection string in Azure Configuration
# - Wrong .NET version in Azure
# - appsettings.json not transformed for production
```

### **Problem 4: Slot swap fails**
**Solution**: Check slot settings match
```bash
# Compare production and staging settings:
az webapp config appsettings list --name myapp --resource-group MyResourceGroup --slot staging
az webapp config appsettings list --name myapp --resource-group MyResourceGroup

# Ensure "sticky" settings (slot settings) are set for things that SHOULDN'T swap
# Like connection strings to different databases for staging vs production
```

---

## 📊 Pipeline Comparison: V1 vs V2

| **Feature** | **V1 (Basic)** | **V2 (Advanced)** |
|-------------|----------------|-------------------|
| **Trigger** | Manual only | Auto on push + manual |
| **Testing** | None | Unit tests before deploy |
| **Environments** | Direct to production | Staging → Production |
| **Downtime** | App restarts (~30s) | Zero downtime (swap) |
| **Rollback** | Manual redeploy | Instant swap back |
| **Safety** | Deploys even if broken | Tests prevent bad deploys |
| **Best For** | Learning/Testing | Production applications |

---

## 🎤 Interview Talking Points

When asked about your pipeline, structure your answer:

### **1. Start with Principles**
> "I follow three core principles: **Safety First** (test before deploy), **Zero Downtime** (blue-green deployment), and **Infrastructure as Code** (everything version controlled)."

### **2. Explain the Architecture**
> "The pipeline has 3 key stages: **Build** (compile + test), **Stage** (deploy to staging slot + validate), and **Release** (swap to production). This ensures only validated code reaches users."

### **3. Highlight Key Features**
> "I implemented **blue-green deployment** using Azure slots for zero-downtime updates, **automated rollback** capability by swapping back, and **environment-specific configurations** to prevent staging from affecting production data."

### **4. Mention Problem Prevention**
> "Common pitfalls I avoid: **caching dependencies** for speed, **failing fast** on tests, using **Service Principals** instead of publish profiles for better security, and **validating health** after deployment."

---

## 📝 Quick Reference Commands

### **GitHub Actions**
```bash
# View workflow runs
gh run list --workflow=deploy.yml

# Rerun a failed workflow
gh run rerun <run-id>

# Download workflow logs
gh run view <run-id> --log
```

### **Azure CLI**
```bash
# List deployments
az webapp deployment list --name myapp --resource-group MyResourceGroup

# Swap slots
az webapp deployment slot swap --name myapp --resource-group MyResourceGroup --slot staging

# View app logs
az webapp log tail --name myapp --resource-group MyResourceGroup

# Delete old deployments
az webapp deployment list --name myapp --query "[?status!='Success'].id" -o tsv | xargs -I {} az webapp deployment delete --ids {}
```

---

## 🚀 Next Steps

### **Week 1**: Implement V1 pipeline, get it working
### **Week 2**: Add testing and caching
### **Week 3**: Implement V2 with staging slot
### **Week 4**: Add production environment with approvals

---

## ❓ Need Help?

1. **Check GitHub Actions logs** - Detailed error messages
2. **Verify Azure resources exist** - Web App, slots
3. **Confirm secrets are correct** - No typos in names/values
4. **Test locally first** - `dotnet publish` should work on your machine

---

*Document Version: 2.0 | Last Updated: [Current Date]*  
*Use this as a living document - update as your pipeline evolves!*

---

## 🎯 TL;DR - Essential Checklist

- [ ] **V1 Working**: Basic deploy from GitHub → Azure
- [ ] **Add Tests**: `dotnet test` in pipeline
- [ ] **Add Caching**: For faster builds
- [ ] **Create Staging Slot**: In Azure
- [ ] **Implement V2**: Build → Deploy to staging → Swap
- [ ] **Set Up GitHub Environments**: With approval gates
- [ ] **Test Rollback**: Swap back if something goes wrong

**Remember**: Start simple, make it work, then add complexity. Each change should be tested before moving to the next!
