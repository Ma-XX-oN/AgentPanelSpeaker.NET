*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-140    permanent    infrastructure

*** Test Cases ***
Isolated Child Timeout Suite Passes
    ${result}=    Run Process    ${APP_EXE}    --test    test-isolation-timeout    stdout=PIPE    stderr=STDOUT
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=AgentPanelSpeaker test-isolation timeout suite failed. Output: ${result.stdout}
    Should Contain
    ...    ${result.stdout}
    ...    PASS  test-isolation/hanging-child-is-bounded
    Should Contain
    ...    ${result.stdout}
    ...    PASS  test-isolation/zero-exit-without-marker-is-rejected
    Should Contain
    ...    ${result.stdout}
    ...    PASS  test-isolation/successful-marker-is-accepted
    Should Contain
    ...    ${result.stdout}
    ...    TEST-SUITE-COMPLETE test-isolation-timeout exit=0
