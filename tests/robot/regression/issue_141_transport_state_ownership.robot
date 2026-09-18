*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-141    permanent    ui    speech    transport

*** Test Cases ***
Transport State Ownership Suite Passes
    ${result}=    Run Process    ${APP_EXE}    --test    transport-state-ownership    stdout=PIPE    stderr=STDOUT
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=AgentPanelSpeaker transport-state suite failed. Output: ${result.stdout}
    Should Contain
    ...    ${result.stdout}
    ...    PASS  transport-state/keyboard-pause-keeps-active-speech-owned
    Should Contain
    ...    ${result.stdout}
    ...    PASS  transport-state/button-pause-keeps-active-speech-owned
    Should Contain
    ...    ${result.stdout}
    ...    TEST-SUITE-COMPLETE transport-state-ownership exit=0
