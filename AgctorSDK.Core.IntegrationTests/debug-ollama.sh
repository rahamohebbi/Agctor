#!/bin/bash

# Ollama Debug Script for AgctorSDK Integration Tests
# Run this script to diagnose Ollama-related issues

echo "=== Ollama Debugging Script ==="
echo "Timestamp: $(date)"
echo

# Check if Ollama is installed
echo "1. Checking Ollama installation..."
if command -v ollama &> /dev/null; then
    echo "✅ Ollama is installed"
    ollama --version
else
    echo "❌ Ollama not found. Please install Ollama first:"
    echo "   Visit: https://ollama.ai"
    exit 1
fi
echo

# Check if Ollama service is running
echo "2. Checking Ollama service status..."
if pgrep -f "ollama serve" > /dev/null; then
    echo "✅ Ollama service is running"
    echo "Process info:"
    ps aux | grep "ollama serve" | grep -v grep
else
    echo "❌ Ollama service is not running"
    echo "💡 Start with: ollama serve"
    echo "   (Run in a separate terminal)"
fi
echo

# Test API connectivity
echo "3. Testing Ollama API connectivity..."
if curl -s -f http://localhost:11434/api/tags > /dev/null; then
    echo "✅ Ollama API is accessible"
    echo "API response:"
    curl -s http://localhost:11434/api/tags | jq . 2>/dev/null || curl -s http://localhost:11434/api/tags
else
    echo "❌ Cannot connect to Ollama API at http://localhost:11434"
    echo "💡 Ensure Ollama is running: ollama serve"
fi
echo

# Check available models
echo "4. Checking available models..."
if command -v ollama &> /dev/null && pgrep -f "ollama serve" > /dev/null; then
    echo "Available models:"
    ollama list
    echo
    
    # Check specifically for mistral
    if ollama list | grep -q "mistral"; then
        echo "✅ Mistral model is available"
    else
        echo "❌ Mistral model not found"
        echo "💡 Pull with: ollama pull mistral"
    fi
else
    echo "⚠️ Cannot check models - Ollama service not running"
fi
echo

# Test model generation
echo "5. Testing model generation (if mistral is available)..."
if ollama list 2>/dev/null | grep -q "mistral"; then
    echo "Testing mistral model generation..."
    echo "Prompt: 'What is 2+2?'"
    
    # Create test request
    TEST_REQUEST='{"model":"mistral","prompt":"What is 2+2? Answer in one sentence.","stream":false}'
    
    echo "Sending test request..."
    RESPONSE=$(curl -s -X POST http://localhost:11434/api/generate \
        -H "Content-Type: application/json" \
        -d "$TEST_REQUEST" \
        --max-time 30)
    
    if [ $? -eq 0 ] && [ -n "$RESPONSE" ]; then
        echo "✅ Model generation test successful"
        echo "Response:"
        echo "$RESPONSE" | jq . 2>/dev/null || echo "$RESPONSE"
    else
        echo "❌ Model generation test failed"
        echo "Response: $RESPONSE"
    fi
else
    echo "⚠️ Skipping generation test - mistral model not available"
fi
echo

# System information
echo "6. System information..."
echo "OS: $(uname -s)"
echo "Architecture: $(uname -m)"
echo "Available memory: $(free -h 2>/dev/null | grep '^Mem:' | awk '{print $7}' || echo 'N/A')"
echo "Disk space: $(df -h . | tail -1 | awk '{print $4}' || echo 'N/A')"
echo

# Port check
echo "7. Checking port 11434..."
if netstat -tuln 2>/dev/null | grep -q ":11434 "; then
    echo "✅ Port 11434 is listening"
    netstat -tuln | grep ":11434 "
elif ss -tuln 2>/dev/null | grep -q ":11434 "; then
    echo "✅ Port 11434 is listening"
    ss -tuln | grep ":11434 "
else
    echo "❌ Port 11434 is not listening"
    echo "💡 Start Ollama service: ollama serve"
fi
echo

echo "=== Debug Summary ==="
echo "If tests are failing, check the items marked with ❌ above."
echo
echo "Common solutions:"
echo "1. Start Ollama: ollama serve"
echo "2. Pull model: ollama pull mistral"
echo "3. Check firewall settings for port 11434"
echo "4. Restart Ollama service if it's running but not responding"
echo
echo "For more help, see: https://github.com/jmorganca/ollama"
