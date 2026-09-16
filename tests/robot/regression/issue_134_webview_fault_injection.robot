*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-134    permanent    ui    diagnostics

*** Test Cases ***
Dedicated WebView Shutdown Fault Injector Exists
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}WebViewShutdownFaultDiagnostic.cs    Test WebView2 shutdown fault
