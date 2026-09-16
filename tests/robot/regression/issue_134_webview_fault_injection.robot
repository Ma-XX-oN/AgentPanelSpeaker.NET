*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-134    permanent    ui    diagnostics

*** Test Cases ***
Dedicated WebView Shutdown Fault Injector Exists
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}WebViewShutdownFaultDiagnostic.cs    Test WebView2 shutdown fault

Production Startup Installs The Diagnostic Control
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}Program.cs    WebViewShutdownFaultDiagnostic.Install(mainForm);

Fault Injector Uses A Real WebView2
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}WebViewShutdownFaultDiagnostic.cs    var webView = new WebView2

Fault Injector Logs The Intentional Request
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}WebViewShutdownFaultDiagnostic.cs    diagnostic.webview_shutdown_fault_requested
