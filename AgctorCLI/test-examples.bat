@echo off
REM Test script demonstrating multiple CLI Agent Runner examples
echo ========================================
echo Testing Agctor CLI Agent Runner Examples
echo ========================================
echo.

echo 1. Business Analysis Task...
call .\run-agent.bat "Analyze Q1 financial performance"
echo.

echo 2. Content Creation Task...
call .\run-agent.bat "Create executive summary for quarterly review"
echo.

echo 3. Planning Task...
call .\run-agent.bat "Develop project timeline for software deployment"
echo.

echo 4. Research Task...
call .\run-agent.bat "Research industry trends in cloud computing"
echo.

echo ========================================
echo All examples completed successfully!
echo ======================================== 