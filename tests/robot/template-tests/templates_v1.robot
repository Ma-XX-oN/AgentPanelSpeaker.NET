*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    template-selftest    template-v1    issue-108

*** Test Cases ***
Command Success Accepts Zero Exit
    Command Should Succeed V1    python    -c    import sys; sys.exit(0)

Command Success Rejects Nonzero Exit
    Run Keyword And Expect Error    *Expected 'python' to exit 0 but got 7.*
    ...    Command Should Succeed V1    python    -c    import sys; sys.exit(7)

Command Success Classifies Missing Program As Infrastructure Error
    Run Keyword And Expect Error    INFRASTRUCTURE ERROR:*
    ...    Command Should Succeed V1    robot-template-intentionally-missing-executable-108

Expected Failure Accepts Matching Reason
    Command Should Fail For Reason V1
    ...    python
    ...    expected-marker
    ...    -c
    ...    import sys; print('expected-marker'); sys.exit(3)

Expected Failure Rejects Unexpected Success
    Run Keyword And Expect Error    *Expected 'python' to fail, but it exited 0.*
    ...    Command Should Fail For Reason V1
    ...    python
    ...    expected-marker
    ...    -c
    ...    print('expected-marker')

Expected Failure Rejects Wrong Reason
    Run Keyword And Expect Error    *Command failed for the wrong reason.*
    ...    Command Should Fail For Reason V1
    ...    python
    ...    expected-marker
    ...    -c
    ...    import sys; print('different-marker'); sys.exit(4)

Path Template Accepts Existing File
    ${path}=    Set Variable    ${CURDIR}${/}templates_v1.robot
    Path Should Exist V1    ${path}

File Contains Template Accepts Matching Text
    ${path}=    Set Variable    ${CURDIR}${/}templates_v1.robot
    File Should Contain Text V1    ${path}    Command Success Accepts Zero Exit

File Regex Template Rejects Missing Pattern
    ${path}=    Set Variable    ${CURDIR}${/}templates_v1.robot
    Run Keyword And Expect Error    *does not match pattern*
    ...    Text File Should Match Regex V1    ${path}    THIS_PATTERN_MUST_NOT_EXIST_108

Relative Difference Accepts Value Inside Limit
    Relative Difference Should Be At Most V1    1.04    1.0    0.05

Relative Difference Rejects Value Outside Limit
    Run Keyword And Expect Error    *Relative difference*exceeds maximum*
    ...    Relative Difference Should Be At Most V1    1.06    1.0    0.05
