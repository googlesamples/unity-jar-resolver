This directory contains Maven packages components to test dependency resolution.

The following test components are present:

Test cases:
  - Case 1: pull A requires common C, push B requires common-impl D
    common C is compatible with common-impl D
  - Case 2: pull A requires common C, push B requires common-impl D
    common C is incompatible with common-impl D
    Version of common X and common-impl Y exist that are compatible with
    both pull A and push B.
  - Case 3: pull A requires common C, push B requires common-impl D
    common C is incompatible with common-impl D
    No major version of common and common-impl exists that are compatible with
    both pull A and push B.
  - Case 4: common A requires common-impl C, common B requires common-impl D
    C & D are not compatible with each other.  The highest version of common needs
    to be selected.

The following section describes the set of artifacts in the form,
group artifact version 'dependencies':
====
org.test.psr common-impl 1.0.0 ''
org.test.psr common 1.0.1 'org.test.psr:common-impl:[1.0.0]'
org.test.psr pull 1.0.2 'org.test.psr:common:[1.0.1]'
org.test.psr push 1.0.3 'org.test.psr:common-impl:[1.0.0]'

org.test.psr common-impl 2.1.0 ''
org.test.psr common 2.2.1 'org.test.psr:common-impl:[2.1.0,2.2.0)'
org.test.psr push 2.0.2 'org.test.psr:common:[2.2.+,2.2.8]'
org.test.psr common-impl 2.3.0 ''
org.test.psr common 2.4.0 'org.test.psr:common-impl:[2.3.0,2.4.0)'
org.test.psr pull 2.0.3 'org.test.psr:common-impl:[2.3.0,2.4.0)'
org.test.psr common-impl 2.5.0 ''
org.test.psr common 2.5.0 'org.test.psr:common-impl:[2.5.0]'
org.test.psr push 2.0.4 'org.test.psr:common:[2.4.0,2.5.0)'

org.test.psr common-impl 3.0.0 ''
org.test.psr common 3.0.1 'org.test.psr:common-impl:[3.0.0,4.0.0)'
org.test.psr common-impl 4.0.0 ''
org.test.psr common 4.0.1 'org.test.psr:common-impl:[4.0.0,5.0.0)'
org.test.psr common-impl 5.0.0 ''
org.test.psr common 5.0.1 'org.test.psr:common-impl:[5.0.0]'
org.test.psr push 5.0.1 'org.test.psr:common:[3.0.0,4.0.0)'
org.test.psr pull 6.0.1 'org.test.psr:common-impl:[4.0.0,5.0.0)'

org.test.psr common-impl 3.0.1 ''
org.test.psr common-impl 3.0.2 ''
org.test.psr common 3.0.2 'org.test.psr:common-impl:[3.0.1]'
org.test.psr common 3.0.3 'org.test.psr:common-impl:[3.0.2]'

org.test.psr.locked common 1.2.3 ''
org.test.psr.locked input 1.2.3 'org.test.psr.locked:common:[1.2.3]'
org.test.psr.locked new-common 1.5.0 ''
org.test.psr.locked input 1.5.0 'org.test.psr.locked:new-common:[1.5.0]'
org.test.psr.locked output 1.5.0 'org.test.psr.locked:new-common:[1.5.0]'
