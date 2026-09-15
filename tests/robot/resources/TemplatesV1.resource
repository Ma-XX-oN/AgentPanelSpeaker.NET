*** Settings ***
Library    OperatingSystem
Library    Process

*** Variables ***
${TEMPLATE_API_VERSION}    1

*** Keywords ***
Command Should Succeed V1
    [Documentation]    Run a checked-in command contract and require exit code zero.
    [Arguments]    ${program}    @{arguments}
    TRY
        ${result}=    Run Process    ${program}    @{arguments}    stdout=PIPE    stderr=STDOUT
    EXCEPT    AS    ${error}
        Fail    INFRASTRUCTURE ERROR: ${error}
    END
    IF    ${result.rc} != 0
        Fail    Expected '${program}' to exit 0 but got ${result.rc}. Output: ${result.stdout}
    END

Command Should Fail For Reason V1
    [Documentation]    Require a nonzero exit and a specific diagnostic fragment.
    [Arguments]    ${program}    ${required_text}    @{arguments}
    TRY
        ${result}=    Run Process    ${program}    @{arguments}    stdout=PIPE    stderr=STDOUT
    EXCEPT    AS    ${error}
        Fail    INFRASTRUCTURE ERROR: ${error}
    END
    IF    ${result.rc} == 0
        Fail    Expected '${program}' to fail, but it exited 0.
    END
    Should Contain    ${result.stdout}    ${required_text}
    ...    msg=Command failed for the wrong reason. Required '${required_text}'. Output: ${result.stdout}

Path Should Exist V1
    [Documentation]    Require a file to exist.
    [Arguments]    ${path}
    OperatingSystem.File Should Exist    ${path}

File Should Contain Text V1
    [Documentation]    Require a UTF-8/text file to contain an exact fragment.
    [Arguments]    ${path}    ${required_text}
    OperatingSystem.File Should Exist    ${path}
    ${content}=    Get File    ${path}
    Should Contain    ${content}    ${required_text}

Text File Should Match Regex V1
    [Documentation]    Require a text file to contain a regular-expression match.
    [Arguments]    ${path}    ${pattern}
    OperatingSystem.File Should Exist    ${path}
    ${content}=    Get File    ${path}
    Should Match Regexp    ${content}    ${pattern}

Relative Difference Should Be At Most V1
    [Documentation]    Compare two numeric values using abs(actual-expected)/max(abs(expected),1e-12).
    [Arguments]    ${actual}    ${expected}    ${maximum_relative_difference}
    ${actual_number}=    Convert To Number    ${actual}
    ${expected_number}=    Convert To Number    ${expected}
    ${limit}=    Convert To Number    ${maximum_relative_difference}
    ${difference}=    Evaluate    abs($actual_number - $expected_number) / max(abs($expected_number), 1e-12)
    IF    ${difference} > ${limit}
        Fail    Relative difference ${difference} exceeds maximum ${limit}; actual=${actual_number}, expected=${expected_number}.
    END
