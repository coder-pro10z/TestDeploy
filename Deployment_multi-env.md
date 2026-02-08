# **Multi-Environment Deployment on Azure Free Tier**
*Using GitHub Actions with Separate Web Apps for Staging & Production*

## **📋 Overview**

This document explains how to implement professional multi-environment deployment using **GitHub Actions** to deploy **.NET applications** to **Azure Web Apps** on the **free tier**. Since free tier doesn't support deployment slots, we use separate web apps for each environment with manual approval gates.

---

## **🏗️ Architecture Design**

### **Free Tier Constraints & Solutions**
```
Azure Free Tier Limitations:
• ❌ No deployment slots (requires Basic B1 tier ~$13/month)
• ❌ No custom domains (except *.azurewebsites.net)
• ⚠️ Apps sleep after 20 minutes inactivity
• ✅ Multiple free web apps allowed (up to 10 per subscription)
• ✅ GitHub Actions included (2,000 minutes/month free)

Our Solution: Separate Web Apps Per Environment
┌─────────────────┐    ┌─────────────────┐    ┌──────────────────┐
│   DEVELOPMENT   │    │    STAGING      │    │   PRODUCTION     │
│  testapp-dev    │    │  testapp-staging│    │  testapp-prod    │
│  *.azurewebsites│    │  *.azurewebsites│    │  *.azurewebsites │
│  Auto-deploy    │    │  Auto-deploy    │    │  Manual Approval │
│  on PR merge    │    │  on main push   │    │  Required        │
└─────────────────┘    └─────────────────┘    └──────────────────┘
```

---

## **🔧 Step 1: Azure Infrastructure Setup**

### **A. Create Resource Group**
```bash
# Create a resource group (free)
az group create \
  --name TestDeploy-RG \
  --location eastus  # Choose closest region
```

### **B. Create App Service Plan (Free Tier F1)**
```bash
# Create FREE tier plan (F1)
az appservice plan create \
  --name TestDeploy-FreePlan \
  --resource-group TestDeploy-RG \
  --sku F1  # Free tier
  --is-linux  # Linux is free, Windows has charges
```

### **C. Create Three Separate Web Apps**
```bash
# 1. DEVELOPMENT Web App
az webapp create \
  --name testapp-dev-12345 \  # Must be globally unique
  --resource-group TestDeploy-RG \
  --plan TestDeploy-FreePlan \
  --runtime "DOTNETCORE:8.0"

# 2. STAGING Web App  
az webapp create \
  --name testapp-staging-12345 \
  --resource-group TestDeploy-RG \
  --plan TestDeploy-FreePlan \
  --runtime "DOTNETCORE:8.0"

# 3. PRODUCTION Web App
az webapp create \
  --name testapp-prod-12345 \
  --resource-group TestDeploy-RG \
  --plan TestDeploy-FreePlan \
  --runtime "DOTNETCORE:8.0"

# Verify all three apps
az webapp list --resource-group TestDeploy-RG --query "[].{Name:name,State:state}" -o table
```

### **D. Create Service Principal for GitHub**
```bash
# Create Service Principal (more secure than publish profiles)
az ad sp create-for-rbac \
  --name "github-testdeploy-sp" \
  --role contributor \
  --scopes /subscriptions/YOUR-SUB-ID/resourceGroups/TestDeploy-RG \
  --sdk-auth

# Output will be JSON. Save ALL of it for GitHub Secrets.
```

---

## **🔐 Step 2: GitHub Repository Configuration**

### **A. Required Secrets** (Repository → Settings → Secrets → Actions)
```yaml
# Add these secrets:
AZURE_CREDENTIALS: (paste entire JSON from Service Principal creation)
AZURE_RESOURCE_GROUP: "TestDeploy-RG"
AZURE_WEBAPP_DEV: "testapp-dev-12345"
AZURE_WEBAPP_STAGING: "testapp-staging-12345"  
AZURE_WEBAPP_PRODUCTION: "testapp-prod-12345"
```

### **B. Configure GitHub Environments** (Repository → Settings → Environments)
```
1. Create "development" environment:
   - No protection rules
   - Add variable: ENVIRONMENT_TYPE = "development"

2. Create "staging" environment:
   - No protection rules (or optional reviewers)
   - Add variable: ENVIRONMENT_TYPE = "staging"

3. Create "production" environment:
   - ✅ "Required reviewers" (add yourself/team)
   - ✅ "Wait timer" (optional: 10 minutes delay)
   - Add variable: ENVIRONMENT_TYPE = "production"
```

---

## **🚀 Step 3: Complete Pipeline Configuration**

Create file: `.github/workflows/multi-env-deploy.yml`

```yaml
name: Multi-Environment Deployment (Free Tier)

on:
  push:
    branches:
      - 'feature/**'  # Triggers development deployment
      - 'main'        # Triggers staging deployment
  pull_request:
    branches: [main]  # Build and test on PRs

# Environment-specific configurations
env:
  DOTNET_VERSION: '8.x'
  BUILD_CONFIGURATION: 'Release'
  RESOURCE_GROUP: 'TestDeploy-RG'
  
  # Web App Names (pulled from secrets for security)
  DEV_APP_NAME: ${{ secrets.AZURE_WEBAPP_DEV }}
  STAGING_APP_NAME: ${{ secrets.AZURE_WEBAPP_STAGING }}
  PROD_APP_NAME: ${{ secrets.AZURE_WEBAPP_PRODUCTION }}

jobs:
  # ------------------------------------------------------------
  # JOB 1: BUILD ONCE - Deploy Everywhere (Build Once Principle)
  # ------------------------------------------------------------
  build:
    name: 🏗️ Build and Package
    runs-on: ubuntu-latest
    
    outputs:
      artifact-name: app-build-${{ github.sha }}
    
    steps:
      - name: 📥 Checkout Code
        uses: actions/checkout@v4
        
      - name: ⚙️ Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
          
      - name: 💾 Cache NuGet Packages
        uses: actions/cache@v3
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
          restore-keys: |
            ${{ runner.os }}-nuget-
            
      - name: 🔨 Build Solution
        run: dotnet build --configuration ${{ env.BUILD_CONFIGURATION }} --no-restore
        
      - name: 🧪 Run Tests
        run: dotnet test --configuration ${{ env.BUILD_CONFIGURATION }} --no-build
        
      - name: 📦 Create Deployment Package
        run: |
          dotnet publish \
            --configuration ${{ env.BUILD_CONFIGURATION }} \
            --no-build \
            --output ./publish \
            --self-contained false
          echo "Build completed at: $(date)" > ./publish/build-info.txt
          
      - name: 💿 Upload Build Artifact
        uses: actions/upload-artifact@v4
        with:
          name: app-build-${{ github.sha }}
          path: ./publish
          retention-days: 7

  # ------------------------------------------------------------
  # JOB 2: DEVELOPMENT DEPLOYMENT (Auto on feature branches)
  # ------------------------------------------------------------
  deploy-development:
    name: 🧪 Deploy to Development
    runs-on: ubuntu-latest
    needs: build
    environment: development
    # Only run on feature branches, not on main
    if: github.ref != 'refs/heads/main'
    
    steps:
      - name: 📥 Download Build Artifact
        uses: actions/download-artifact@v4
        with:
          name: app-build-${{ github.sha }}
          
      - name: 🔐 Login to Azure
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
          
      - name: 🎯 Deploy to Development Web App
        uses: azure/webapps-deploy@v2
        with:
          app-name: ${{ env.DEV_APP_NAME }}
          resource-group: ${{ env.RESOURCE_GROUP }}
          package: .
          
      - name: 🔔 Notify Development Deployment
        run: |
          echo "✅ Development deployment complete!"
          echo "URL: https://${{ env.DEV_APP_NAME }}.azurewebsites.net"
          echo "Commit: ${{ github.sha }}"
          echo "Branch: ${{ github.ref }}"

  # ------------------------------------------------------------
  # JOB 3: STAGING DEPLOYMENT (Auto on main branch)
  # ------------------------------------------------------------
  deploy-staging:
    name: 🚦 Deploy to Staging
    runs-on: ubuntu-latest
    needs: build
    environment: staging
    # Only run on main branch
    if: github.ref == 'refs/heads/main'
    
    steps:
      - name: 📥 Download Build Artifact
        uses: actions/download-artifact@v4
        with:
          name: app-build-${{ github.sha }}
          
      - name: 🔐 Login to Azure
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
          
      - name: 🎯 Deploy to Staging Web App
        uses: azure/webapps-deploy@v2
        with:
          app-name: ${{ env.STAGING_APP_NAME }}
          resource-group: ${{ env.RESOURCE_GROUP }}
          package: .
          
      - name: ✅ Run Integration Tests on Staging
        run: |
          echo "Running staging validation tests..."
          STAGING_URL="https://${{ env.STAGING_APP_NAME }}.azurewebsites.net"
          
          # Health check
          if curl -f -s "$STAGING_URL" > /dev/null; then
            echo "✅ Staging health check passed"
          else
            echo "❌ Staging health check failed"
            exit 1
          fi
          
          # Add more automated tests here
          # Example: curl "$STAGING_URL/api/health" | jq '.status == "healthy"'
          
      - name: 📝 Create Deployment Summary
        run: |
          echo "## 🚀 Staging Deployment Ready" >> $GITHUB_STEP_SUMMARY
          echo "**Application:** ${{ env.STAGING_APP_NAME }}" >> $GITHUB_STEP_SUMMARY
          echo "**URL:** https://${{ env.STAGING_APP_NAME }}.azurewebsites.net" >> $GITHUB_STEP_SUMMARY
          echo "**Commit:** ${{ github.sha }}" >> $GITHUB_STEP_SUMMARY
          echo "**Validation:** ✅ All tests passed" >> $GITHUB_STEP_SUMMARY
          echo "**Next Step:** Approve production deployment" >> $GITHUB_STEP_SUMMARY

  # ------------------------------------------------------------
  # JOB 4: PRODUCTION DEPLOYMENT (Manual Approval Required)
  # ------------------------------------------------------------
  deploy-production:
    name: 🚀 Deploy to Production
    runs-on: ubuntu-latest
    needs: deploy-staging  # Must have successful staging deployment
    environment: production  # 🔐 Requires manual approval
    if: github.ref == 'refs/heads/main'
    
    steps:
      - name: 📥 Download Build Artifact
        uses: actions/download-artifact@v4
        with:
          name: app-build-${{ github.sha }}
          
      - name: 🔐 Login to Azure
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
          
      - name: ⚠️ Announce Maintenance Window
        run: |
          echo "========================================"
          echo "PRODUCTION DEPLOYMENT STARTING"
          echo "Time: $(date)"
          echo "Expected downtime: 30-60 seconds"
          echo "Production URL: https://${{ env.PROD_APP_NAME }}.azurewebsites.net"
          echo "========================================"
          
      - name: 🎯 Deploy to Production Web App
        uses: azure/webapps-deploy@v2
        with:
          app-name: ${{ env.PROD_APP_NAME }}
          resource-group: ${{ env.RESOURCE_GROUP }}
          package: .
          
      - name: 🏃‍♂️ Warm-up Production App (Free Tier Needs This)
        run: |
          echo "Warming up production app (free tier cold start)..."
          PROD_URL="https://${{ env.PROD_APP_NAME }}.azurewebsites.net"
          
          # Try up to 5 times with delay
          for i in {1..5}; do
            echo "Warm-up attempt $i/5..."
            STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$PROD_URL" || echo "000")
            
            if [ "$STATUS" = "200" ]; then
              echo "✅ Production app is responding (HTTP 200)"
              break
            fi
            
            echo "Response: HTTP $STATUS, retrying in 10 seconds..."
            sleep 10
          done
          
          if [ "$STATUS" != "200" ]; then
            echo "❌ Production app not responding after 5 attempts"
            exit 1
          fi
          
      - name: 📊 Verify Production Deployment
        run: |
          PROD_URL="https://${{ env.PROD_APP_NAME }}.azurewebsites.net"
          BUILD_INFO=$(curl -s "$PROD_URL/build-info.txt" || echo "No build info")
          
          echo "========================================"
          echo "✅ PRODUCTION DEPLOYMENT SUCCESSFUL"
          echo "URL: $PROD_URL"
          echo "Build Info: $BUILD_INFO"
          echo "Deployment Time: $(date)"
          echo "========================================"
          
      - name: 📢 Send Deployment Notification (Optional)
        if: success()
        run: |
          # Example: Send to Slack/Teams webhook
          curl -X POST -H 'Content-type: application/json' \
            --data "{\"text\":\"✅ Production deployment complete for ${{ github.repository }}. Commit: ${{ github.sha }}\"}" \
            ${{ secrets.SLACK_WEBHOOK_URL }} || echo "No webhook configured"

  # ------------------------------------------------------------
  # OPTIONAL JOB: Keep Apps Warm (Prevent Free Tier Sleep)
  # ------------------------------------------------------------
  keep-warm:
    name: 🔥 Keep Apps Warm
    runs-on: ubuntu-latest
    # Run on schedule (every 5 minutes during business hours)
    if: false  # Disabled by default, enable when needed
    
    steps:
      - name: Ping Development
        run: curl -s "https://${{ env.DEV_APP_NAME }}.azurewebsites.net" > /dev/null || true
        
      - name: Ping Staging
        run: curl -s "https://${{ env.STAGING_APP_NAME }}.azurewebsites.net" > /dev/null || true
        
      - name: Ping Production
        run: curl -s "https://${{ env.PROD_APP_NAME }}.azurewebsites.net" > /dev/null || true
```

---

## **🔍 Step 4: Branch Strategy & Workflow**

### **Development Workflow**
```
1. Create feature branch from main
   git checkout -b feature/new-login

2. Work on feature, commit changes
   git commit -m "Add new login feature"

3. Push to GitHub (triggers development deployment)
   git push origin feature/new-login
   → Auto-deploys to: testapp-dev-12345.azurewebsites.net

4. Create Pull Request to main
   → Triggers build & tests (no deployment)

5. Merge PR to main
   → Auto-deploys to: testapp-staging-12345.azurewebsites.net
   → Requires manual approval for production
```

### **Production Promotion Process**
```
GitHub Actions Dashboard:
1. ✅ Build job completes
2. ✅ Staging deployment completes
3. ⏸️ Production deployment WAITING (yellow dot)

Manual Approval Required:
1. Go to: GitHub → Actions → Current workflow run
2. Click "Review deployments" on production job
3. Click "Approve" (or "Reject")
4. Production deployment proceeds automatically
```

---

## **⚙️ Step 5: Environment-Specific Configuration**

### **App Settings Per Environment**
```bash
# Development (testapp-dev-12345)
az webapp config appsettings set \
  --name testapp-dev-12345 \
  --resource-group TestDeploy-RG \
  --settings \
    ASPNETCORE_ENVIRONMENT="Development" \
    DatabaseConnection="Server=dev-db;Database=MyApp_Dev" \
    LogLevel="Debug"

# Staging (testapp-staging-12345)
az webapp config appsettings set \
  --name testapp-staging-12345 \
  --resource-group TestDeploy-RG \
  --settings \
    ASPNETCORE_ENVIRONMENT="Staging" \
    DatabaseConnection="Server=staging-db;Database=MyApp_Staging" \
    LogLevel="Information"

# Production (testapp-prod-12345)
az webapp config appsettings set \
  --name testapp-prod-12345 \
  --resource-group TestDeploy-RG \
  --settings \
    ASPNETCORE_ENVIRONMENT="Production" \
    DatabaseConnection="Server=prod-db;Database=MyApp_Prod" \
    LogLevel="Warning"
```

---

## **🔧 Step 6: Database Migrations (Optional)**

Add to pipeline before deployment:

```yaml
- name: Run Database Migrations
  run: |
    # Apply EF Core migrations to appropriate database
    CONNECTION_STRING="${{ secrets[format('DB_CONNECTION_{0}', env.ENVIRONMENT_TYPE)] }}"
    
    dotnet ef database update \
      --project ./src/MyApp.Data \
      --connection "$CONNECTION_STRING" \
      --verbose
      
  # Set different DB connection secrets per environment:
  # DB_CONNECTION_development, DB_CONNECTION_staging, DB_CONNECTION_production
```

---

## **🚨 Troubleshooting Free Tier Issues**

### **Problem 1: App Sleeping (Cold Start)**
**Symptoms**: First request after 20+ minutes takes 30-60 seconds
**Solution**: 
```yaml
# Add to production deployment job:
- name: Warm-up after deployment
  run: |
    # Make 3 quick requests to wake app
    for i in {1..3}; do
      curl -s "https://${{ env.PROD_APP_NAME }}.azurewebsites.net" > /dev/null
      sleep 5
    done
```

### **Problem 2: Out of Free Tier Minutes**
**Limit**: 1,000 minutes/month per subscription on F1 tier
**Monitoring**:
```bash
# Check usage
az consumption usage list \
  --start-date $(date -d "-30 days" +%Y-%m-%d) \
  --query "[?contains(meterDetails.meterName, 'Free')].{Date:usageStart, Usage:usageQuantity}" \
  -o table
```

### **Problem 3: Web App Name Already Taken**
**Solution**: Add unique suffix (timestamp, random)
```bash
# Use timestamp for uniqueness
UNIQUE_SUFFIX=$(date +%Y%m%d%H%M%S)
APP_NAME="testapp-prod-$UNIQUE_SUFFIX"
```

---

## **📊 Monitoring & Observability**

### **Add Application Insights (Free)**
```bash
# Create Application Insights resource (free up to 5GB/month)
az monitor app-insights component create \
  --app testapp-insights \
  --resource-group TestDeploy-RG \
  --location eastus

# Connect to each web app
az webapp config appsettings set \
  --name testapp-prod-12345 \
  --resource-group TestDeploy-RG \
  --settings \
    APPINSIGHTS_INSTRUMENTATIONKEY="your-instrumentation-key"
```

### **Deployment Verification Script**
```bash
#!/bin/bash
# verify-deployment.sh

ENV=$1
APP_NAME=""

case $ENV in
  dev) APP_NAME="testapp-dev-12345" ;;
  staging) APP_NAME="testapp-staging-12345" ;;
  prod) APP_NAME="testapp-prod-12345" ;;
  *) echo "Invalid environment"; exit 1 ;;
esac

URL="https://${APP_NAME}.azurewebsites.net"

echo "Testing $ENV environment ($URL)"
echo "=============================="

# Check HTTP response
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$URL")
echo "HTTP Status: $HTTP_CODE"

# Check response time
TIME=$(curl -s -o /dev/null -w "%{time_total}s" "$URL")
echo "Response Time: $TIME"

# Check build info (if you added build-info.txt)
BUILD_INFO=$(curl -s "${URL}/build-info.txt" || echo "No build info")
echo "Build Info: $BUILD_INFO"

# Check app settings (via API if configured)
echo "Environment: $ENV"
```

---

## **🎯 Best Practices for Free Tier**

### **1. Cost Optimization**
```yaml
# Use Azure CLI to check and manage costs
- name: Monitor Free Tier Usage
  run: |
    # Get remaining free days
    az rest --method get \
      --url "https://management.azure.com/subscriptions/{sub-id}/providers/Microsoft.Web/publishingUsers/web?api-version=2022-03-01" \
      --query "properties.freeSiteRemaining"
```

### **2. Performance Optimization**
```yaml
# Optimize .NET publish for faster deployment
- name: Optimize Publish
  run: |
    dotnet publish -c Release \
      --no-self-contained \  # Smaller package
      --output ./publish \
      /p:DebugType=None \
      /p:DebugSymbols=false \
      /p:EnableCompressionInSingleFile=true
```

### **3. Security**
```yaml
# Regular security scanning
- name: Security Scan
  run: |
    # Check for vulnerable dependencies
    dotnet list package --vulnerable --include-transitive
    
    # Check for outdated packages
    dotnet outdated --update
```

---

## **📈 Upgrade Path to Basic Tier**

When ready to upgrade (enables slots, custom domains):

```bash
# 1. Scale up from F1 to B1
az appservice plan update \
  --name TestDeploy-FreePlan \
  --resource-group TestDeploy-RG \
  --sku B1  # ~$13/month

# 2. Create deployment slots
az webapp deployment slot create \
  --name testapp-prod-12345 \
  --resource-group TestDeploy-RG \
  --slot staging

# 3. Update pipeline to use slots instead of separate apps
# Change from:
#   app-name: ${{ env.STAGING_APP_NAME }}
# To:
#   app-name: ${{ env.PROD_APP_NAME }}
#   slot-name: 'staging'
#   action: swap
```

---

## **✅ Quick Start Checklist**

- [ ] **Azure Setup**
  - [ ] Resource group created
  - [ ] Free App Service Plan (F1) created
  - [ ] 3 web apps created (dev, staging, prod)
  - [ ] Service Principal created for GitHub

- [ ] **GitHub Configuration**
  - [ ] Secrets added (AZURE_CREDENTIALS, app names)
  - [ ] Environments created (dev, staging, prod)
  - [ ] Production environment has required reviewers

- [ ] **Pipeline Setup**
  - [ ] `.github/workflows/multi-env-deploy.yml` created
  - [ ] Pipeline triggers configured correctly
  - [ ] Branch protection rules set (optional)

- [ ] **Testing**
  - [ ] Push to feature branch → deploys to dev
  - [ ] Merge to main → deploys to staging
  - [ ] Manual approval → deploys to production
  - [ ] All URLs accessible

---

## **📞 Support & Resources**

- **Azure Free Tier Limits**: https://azure.microsoft.com/free/
- **GitHub Actions Pricing**: https://docs.github.com/en/billing
- **.NET on Azure Documentation**: https://docs.microsoft.com/dotnet/azure/
- **Troubleshooting Guide**: Check GitHub Actions logs for detailed errors

---

*Document Version: 2.1 | Last Updated: [Current Date]*  
*This document should be updated when: Azure pricing changes, GitHub Actions features are added, or .NET version is updated.*
