## Distinct behavior or problem

Describe the interoperability behavior or defect this change makes executable.

## Change

Describe the focused implementation and expected result.

## Sanitization and security

- [ ] No raw device export, credential, address, serial number, person, face, plate, media, installation URL, or local path is included
- [ ] Fixture values are synthetic or irreversibly reduced and the manifest note is accurate
- [ ] Offline and bounded behavior is preserved

## Verification

- [ ] Locked restore, Release build, and all tests pass
- [ ] `verify` passes and `COMPATIBILITY.md` is current
- [ ] Formatting and vulnerable-package audit pass
- [ ] Documentation is updated when behavior or limits change
