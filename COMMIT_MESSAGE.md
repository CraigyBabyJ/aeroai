# Commit Message for Recent Changes

## Summary
Added WPF UI, updated fine-tuned model configuration, and improved SimBrief integration

## Changes Made

### 1. WPF UI Implementation (AeroAI.UI/)
- Created new WPF project with dark theme matching SayIntentions style
- MainWindow.xaml: Full UI layout with COM/XPDR bar, airport info, message history, input field
- MainWindow.xaml.cs: Event handlers and ATC service integration
- SimBriefDialog: Dialog for importing flight plans with username persistence
- AtcService.cs: Service layer bridging UI to ATC engine

### 2. Model Configuration Updates
- Updated default OpenAI model to fine-tuned v3: `ft:gpt-4o-mini-2024-07-18:personal:aeroai-v3:Cm6OllUu`
- Changed default system prompt to compressed version for fine-tuned model
- EnvironmentConfig.cs: Updated GetOpenAiModel() and GetSystemPromptPath() defaults

### 3. SimBrief Integration Improvements
- SimBriefDialog now loads Pilot ID from userconfig.json on open
- Saves Pilot ID back to userconfig.json after import
- Handles multiple file path locations (current dir, parent, project root)

### 4. Project Structure
- Created AeroAI.UI.csproj with WPF support
- Added solution file (AeroAI.sln)
- Configured to include source files from parent project
- Disabled Git source link to avoid build errors

### 5. Documentation
- Updated agent.md with XTTS v2 setup instructions (later removed)
- Added TTS provider switching documentation

## Files Added
- AeroAI.UI/ (entire new project)
- AeroAI.sln
- AeroAI/Config/EnvironmentConfig.cs (updated)

## Files Modified
- AeroAI/Config/EnvironmentConfig.cs
- agent.md

## Files Removed (XTTS experiment)
- tts_server/ (removed - user decided to stick with OpenAI TTS)

