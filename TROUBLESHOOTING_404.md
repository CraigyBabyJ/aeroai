# Troubleshooting: OpenAI API 404 Error

## Problem
You're seeing an error like:
```
ERROR: Failed to communicate with OpenAI API: Response status code does not indicate success: 404 (Not Found).
```

## ✅ FIXED: URL Construction Issue

**The client now hardcodes the correct base URL** to prevent double `/v1` or missing `/v1` issues:

- **Base URL**: `https://api.openai.com/v1/` (with trailing slash)
- **Endpoint**: `chat/completions` (no leading slash)
- **Final URL**: `https://api.openai.com/v1/chat/completions` ✓

The `OPENAI_BASE_URL` environment variable is now ignored for reliability. The client always uses the correct hardcoded URL.

## Common Causes

### 1. Proxy/VPN/Network Interception (nginx 404 errors)
**If you see an nginx HTML error page**, your request is being intercepted before reaching OpenAI's servers.

**Symptoms:**
- Error response contains `<html>`, `<center>`, or "nginx" text
- Getting HTML instead of JSON error responses
- 404 from nginx instead of OpenAI API

**Solutions:**
- **Disable VPN** if you're using one
- **Check proxy settings** - ensure no HTTP proxy is intercepting `api.openai.com`
- **Check firewall/antivirus** - some security software blocks API calls
- **Try a different network** (mobile hotspot) to test if it's network-specific
- **Check DNS** - ensure `api.openai.com` resolves correctly:
  ```bash
  nslookup api.openai.com
  ```

### 2. Incorrect Base URL Configuration
The most common cause is an incorrect `OPENAI_BASE_URL` in your `.env` file.

**Correct format:**
```env
OPENAI_BASE_URL=https://api.openai.com/v1
```

**Common mistakes:**
- Missing `/v1`: `OPENAI_BASE_URL=https://api.openai.com` ❌
- Extra trailing slash: `OPENAI_BASE_URL=https://api.openai.com/v1/` ❌ (this is usually OK, but best to avoid)
- Wrong domain: `OPENAI_BASE_URL=https://api.openai.com/v2` ❌

### 2. Missing .env File
If you don't have a `.env` file, the app will use the default base URL (`https://api.openai.com/v1`), which should work. However, you still need to set `OPENAI_API_KEY`.

### 3. Invalid API Key
While a 404 usually indicates a URL problem, an invalid API key can sometimes cause routing issues.

## How to Fix

### Step 1: Check Your .env File
Create or edit your `.env` file in the project root:

```env
OPENAI_API_KEY=sk-your-actual-key-here
OPENAI_MODEL=gpt-4o-mini
OPENAI_BASE_URL=https://api.openai.com/v1
```

**Important:** 
- The base URL **must** end with `/v1` (not `/v2` or anything else)
- Do **not** include a trailing slash after `/v1`
- The API key must start with `sk-` or `sk-proj-`

### Step 2: Verify the Configuration
The improved error handling will now show you the exact URL being called. Look for a line like:
```
URL: https://api.openai.com/v1/chat/completions
```

If the URL doesn't end with `/v1/chat/completions`, your base URL is incorrect.

### Step 3: Test Your API Key
You can test your API key using curl:

```bash
curl https://api.openai.com/v1/models \
  -H "Authorization: Bearer sk-your-key-here"
```

If this returns a 404, your API key might be invalid or the endpoint might have changed.

### Step 4: Check for Proxy/Network Issues
If you're behind a corporate proxy or using a VPN, ensure it's not interfering with API calls to `api.openai.com`.

## Example .env File

```env
# OpenAI Configuration
OPENAI_API_KEY=sk-proj-ABC123...XYZ789
OPENAI_MODEL=gpt-4o-mini
OPENAI_BASE_URL=https://api.openai.com/v1

# Optional: Custom system prompt path
# AEROAI_SYSTEM_PROMPT_PATH=prompts/aeroai_system_prompt.txt
```

## Still Having Issues?

1. **Check the error message** - The improved error handling now shows:
   - The exact URL being called
   - The HTTP status code
   - Error details from the API response

2. **Verify your API key** at https://platform.openai.com/api-keys

3. **Check OpenAI status** at https://status.openai.com/ to ensure their API is operational

4. **Review the full error output** - Look for the "URL:" line in the error message to see exactly what endpoint is being called.

