"""Exercise deployment transactions and development database snapshots."""

import fcntl
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


REPOSITORY = Path(__file__).resolve().parents[1]
MOCK_COMMAND = r'''
import json, os, pathlib, sys
directory = pathlib.Path(os.environ['BESTEST_TEST_DIRECTORY'])
state_path = directory / 'state.json'
state = json.loads(state_path.read_text())
name = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
with (directory / 'commands.jsonl').open('a') as output:
    output.write(json.dumps({'command': name, 'args': args, 'active': state['active']}) + '\n')
result = 0
if name == 'systemctl':
    if 'show-environment' in args:
        result = int(bool(os.environ.get('BESTEST_TEST_NO_SESSION')))
    elif 'is-active' in args:
        result = 0 if state['active'] else 3
    elif 'is-enabled' in args:
        result = 0 if state['enabled'] else 1
    elif 'stop' in args:
        state['active'] = False
    elif 'enable' in args:
        state['enabled'] = True
    elif 'disable' in args:
        state['enabled'] = False
    elif 'start' in args:
        if os.environ.get('BESTEST_TEST_START_FAIL') and not state.get('failed_once'):
            state['failed_once'] = True
            result = 1
        else:
            state['active'] = True
elif name == 'dotnet':
    if args[0] == os.environ.get('BESTEST_TEST_BUILD_FAIL'):
        result = 1
    elif args[0] == 'publish':
        release = pathlib.Path(args[args.index('--output') + 1])
        state['release_id'] = release.name
        (release / 'BestestGame.dll').write_text('new published application')
        (release / 'appsettings.json').write_text('{"DatabasePath": "original source path"}')
        (release / 'wwwroot').mkdir()
        (release / 'wwwroot/app.css').write_text('published CSS')
elif name == 'curl':
    if os.environ.get('BESTEST_TEST_HTTP_FAIL'):
        state['active'] = False
        result = 22
    elif not state.get('http_attempted'):
        state['http_attempted'] = True
        result = 7
    elif args[-1].endswith('/healthz'):
        if os.environ.get('BESTEST_TEST_WRONG_RELEASE'):
            print('some-other-instance')
            state['active'] = False
        else:
            print(state['release_id'])
elif name == 'journalctl':
    print('simulated startup logs')
elif name == 'loginctl':
    print(os.environ.get('BESTEST_TEST_LINGER', 'no'))
state_path.write_text(json.dumps(state))
sys.exit(result)
'''


class StartupChecks(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='bestestgame-startup-')
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.checkout = self.directory / 'checkout with spaces 50% "quote" $cash '
        project = self.checkout / 'BestestGame'
        (project / 'obj').mkdir(parents=True)
        (project / 'BestestGame.csproj').write_text('<Project />')
        (project / 'appsettings.json').write_text(json.dumps({'DatabasePath': '../../Database/data.json'}))
        for name in ('update-live.sh', 'install-startup.sh', 'develop.sh', 'launch.sh', 'refresh-dev-data.py'):
            shutil.copy2(REPOSITORY / name, self.checkout / name)
        self.runtime = self.directory / 'BestestGameLive'
        self.previous = self.runtime / 'releases/old'
        self.previous.mkdir(parents=True)
        (self.previous / 'BestestGame.dll').write_text('old application')
        (self.runtime / 'current').symlink_to(self.previous)
        self.config = self.directory / 'config'
        self.unit = self.config / 'systemd/user/bestestgame.service'
        self.unit.parent.mkdir(parents=True)
        self.previous_unit = '[Unit]\nDescription=Previous installed service\n'
        self.unit.write_text(self.previous_unit)
        self.database = self.runtime / 'data/data.json'
        self.database.parent.mkdir()
        self.database.write_text('{"live-data": "must survive updates"}\n')
        self.legacy_database = self.directory / 'Database/data.json'
        self.legacy_database.parent.mkdir()
        self.legacy_database.write_text('{"live-data": "must survive updates"}\n')
        self.development_database = self.checkout / '.dev-data/data.json'
        self.development_database.parent.mkdir()
        self.development_database.write_text('{"development": "never deploy this"}\n')
        self.state_path = self.directory / 'state.json'
        self.state_path.write_text(json.dumps({'active': True, 'enabled': True}))
        mock_bin = self.directory / 'commands'
        mock_bin.mkdir()
        for name in ('systemctl', 'dotnet', 'curl', 'journalctl', 'sleep', 'sudo', 'loginctl', 'xdg-open'):
            executable = mock_bin / name
            executable.write_text(f'#!{sys.executable}\n' + MOCK_COMMAND)
            executable.chmod(0o755)
        self.env = dict(os.environ, PATH=f'{mock_bin}:/usr/bin:/bin',
                        XDG_CONFIG_HOME=str(self.config), BESTEST_TEST_DIRECTORY=str(self.directory))

    def run_script(self, script='update-live.sh', args=(), **environment):
        result = subprocess.run([str(self.checkout / script), *args],
                                env=dict(self.env, **environment), text=True,
                                capture_output=True, timeout=15)
        return result

    def commands(self):
        path = self.directory / 'commands.jsonl'
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def assert_previous_running(self):
        self.assertEqual((self.runtime / 'current').resolve(), self.previous)
        self.assertEqual(self.unit.read_text(), self.previous_unit)
        self.assertTrue(json.loads(self.state_path.read_text())['active'])
        self.assertEqual(self.database.read_text(), '{"live-data": "must survive updates"}\n')

    def test_update_builds_before_stopping_and_preserves_data(self):
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotEqual((self.runtime / 'current').resolve(), self.previous)
        self.assertEqual((self.previous / 'BestestGame.dll').read_text(), 'old application')
        self.assertEqual(self.database.read_text(), '{"live-data": "must survive updates"}\n')
        backups = list((self.runtime / 'backups').glob('*.json'))
        self.assertEqual(len(backups), 1)
        self.assertEqual(backups[0].read_bytes(), self.database.read_bytes())
        self.assertEqual(self.development_database.read_text(), '{"development": "never deploy this"}\n')
        unit = self.unit.read_text()
        self.assertIn('ASPNETCORE_ENVIRONMENT=Production', unit)
        self.assertIn('DOTNET_ENVIRONMENT=Production', unit)
        self.assertIn(f'WorkingDirectory={self.runtime}/current/', unit)
        self.assertIn('http://localhost:5231', unit)
        self.assertNotIn('launch.sh', unit)
        self.assertNotIn(str(self.checkout), unit)
        settings = json.loads((self.runtime / 'current/appsettings.json').read_text())
        self.assertEqual(settings['DatabasePath'], str(self.database))
        self.assertFalse(self.runtime.is_relative_to(self.checkout))
        commands = self.commands()
        builds = [index for index, command in enumerate(commands) if command['command'] == 'dotnet']
        stop = next(index for index, command in enumerate(commands) if 'stop' in command['args'])
        self.assertTrue(all(index < stop and commands[index]['active'] for index in builds))
        self.assertTrue(all('--artifacts-path' in commands[index]['args'] for index in builds))
        self.assertFalse(list(self.runtime.glob('build.*')))
        second = self.run_script()
        self.assertEqual(second.returncode, 0, second.stderr)
        self.assertEqual(len(list((self.runtime / 'backups').glob('*.json'))), 2)

    def test_failed_checks_or_publish_leave_live_service_untouched(self):
        for stage in ('run', 'publish'):
            with self.subTest(stage=stage):
                result = self.run_script(BESTEST_TEST_BUILD_FAIL=stage)
                self.assertNotEqual(result.returncode, 0)
                self.assert_previous_running()
                self.assertFalse(any('stop' in command['args'] for command in self.commands()))
                self.assertFalse(list(self.runtime.glob('build.*')))

    def test_failed_start_restores_previous_release_and_service(self):
        result = self.run_script(BESTEST_TEST_START_FAIL='1')
        self.assertNotEqual(result.returncode, 0)
        self.assert_previous_running()
        self.assertIn('Previous service configuration restored', result.stderr)

    def test_failed_http_check_restores_previous_release(self):
        result = self.run_script(BESTEST_TEST_HTTP_FAIL='1')
        self.assertNotEqual(result.returncode, 0)
        self.assert_previous_running()
        self.assertIn('simulated startup logs', result.stderr)

    def test_legacy_service_migrates_without_changing_live_database(self):
        (self.runtime / 'current').unlink()
        self.database.unlink()
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue((self.runtime / 'current').is_symlink())
        self.assertIn(str(self.database), self.unit.read_text())
        self.assertEqual(self.database.read_text(), '{"live-data": "must survive updates"}\n')
        self.assertEqual(self.database.read_bytes(), self.legacy_database.read_bytes())

    def test_http_response_from_wrong_release_is_not_success(self):
        result = self.run_script(BESTEST_TEST_WRONG_RELEASE='1')
        self.assertNotEqual(result.returncode, 0)
        self.assert_previous_running()

    def test_failed_legacy_migration_restores_source_service(self):
        (self.runtime / 'current').unlink()
        self.database.unlink()
        result = self.run_script(BESTEST_TEST_START_FAIL='1')
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.runtime / 'current').is_symlink())
        self.assertEqual(self.unit.read_text(), self.previous_unit)
        self.assertTrue(json.loads(self.state_path.read_text())['active'])
        self.assertFalse(self.database.exists())
        self.assertEqual(self.legacy_database.read_text(), '{"live-data": "must survive updates"}\n')

        # A retry must copy the latest legacy data, not reuse the failed attempt's snapshot.
        self.legacy_database.write_text('{"live-data": "new score after failed migration"}\n')
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.database.read_bytes(), self.legacy_database.read_bytes())

    def test_failed_first_install_removes_new_configuration(self):
        self.unit.unlink()
        (self.runtime / 'current').unlink()
        self.database.unlink()
        self.state_path.write_text(json.dumps({'active': False, 'enabled': False}))
        result = self.run_script(BESTEST_TEST_START_FAIL='1')
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.unit.exists())
        self.assertFalse((self.runtime / 'current').is_symlink())
        state = json.loads(self.state_path.read_text())
        self.assertFalse(state['active'] or state['enabled'])

    def test_missing_session_cannot_change_service(self):
        result = self.run_script(BESTEST_TEST_NO_SESSION='1')
        self.assertNotEqual(result.returncode, 0)
        self.assert_previous_running()

    def test_concurrent_update_cannot_touch_service(self):
        with (self.runtime / 'update.lock').open('w') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assert_previous_running()
        self.assertFalse(any(command['command'] == 'dotnet' for command in self.commands()))

    def test_boot_installer_enables_lingering_only_after_success(self):
        result = self.run_script('install-startup.sh', ['--boot'])
        self.assertEqual(result.returncode, 0, result.stderr)
        sudo_calls = [command for command in self.commands() if command['command'] == 'sudo']
        self.assertEqual(len(sudo_calls), 1)
        self.assertIn('enable-linger', sudo_calls[0]['args'])
        self.assertTrue(sudo_calls[0]['active'])

    def test_live_launcher_opens_service_without_building(self):
        result = self.run_script('launch.sh', ['--no-browser'])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(any(command['command'] == 'dotnet' for command in self.commands()))
        self.assertEqual((self.runtime / 'current').resolve(), self.previous)

    def test_source_and_development_database_changes_cannot_overwrite_live_data(self):
        self.legacy_database.write_text('{"development-source": "do not deploy this"}')
        (self.checkout / 'BestestGame/appsettings.json').write_text(json.dumps({
            'DatabasePath': str(self.development_database),
        }))
        result = self.run_script(args=['--wait'])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.database.read_text(), '{"live-data": "must survive updates"}\n')

    def test_missing_deployed_database_is_not_replaced_with_legacy_data(self):
        self.database.unlink()
        result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('live database is missing', result.stderr)
        self.assertFalse(self.database.exists())
        self.assertFalse(any(command['command'] == 'dotnet' for command in self.commands()))

    def test_command_line_development_copies_live_data_before_launch(self):
        result = self.run_script('develop.sh', ['--no-browser'])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.development_database.read_bytes(), self.database.read_bytes())
        dotnet = [command for command in self.commands() if command['command'] == 'dotnet']
        self.assertEqual(len(dotnet), 1)
        self.assertEqual(dotnet[0]['args'][0], 'watch')
        self.assertTrue(json.loads(self.state_path.read_text())['active'])


class DevelopmentDataChecks(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix='bestestgame-debug-data-')
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.checkout = self.directory / 'checkout with spaces "quote" $cash'
        self.project = self.checkout / 'BestestGame'
        self.project.mkdir(parents=True)
        self.config = self.project / 'appsettings.json'
        self.config.write_text(json.dumps({'DatabasePath': '../../Database/data.json'}))
        self.script = self.checkout / 'refresh-dev-data.py'
        shutil.copy2(REPOSITORY / 'refresh-dev-data.py', self.script)
        self.live = self.directory / 'Database/data.json'
        self.live.parent.mkdir()
        self.snapshot = b'{"Tournaments": [{"Name": "Live tournament"}], "Duels": []}\n'
        self.live.write_bytes(self.snapshot)
        self.development = self.checkout / '.dev-data/data.json'

    def run_script(self):
        return subprocess.run([sys.executable, str(self.script)], cwd=self.directory,
                              text=True, capture_output=True, timeout=5)

    def assert_only_development_database(self):
        self.assertEqual(list(self.development.parent.iterdir()), [self.development])

    def test_creates_independent_copy_and_resets_previous_debug_changes(self):
        for attempt in range(2):
            result = self.run_script()
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(self.development.read_bytes(), self.snapshot)
            self.assertFalse(os.path.samefile(self.live, self.development))
            self.assert_only_development_database()
            self.development.write_text('{"debugging": "changed data"}')
            self.assertEqual(self.live.read_bytes(), self.snapshot)

    def test_each_refresh_uses_current_live_data(self):
        self.assertEqual(self.run_script().returncode, 0)
        self.live.write_text('{"Tournaments": [{"Name": "New live tournament"}]}')
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.development.read_bytes(), self.live.read_bytes())

    def test_refresh_uses_sibling_live_database_instead_of_source_configuration(self):
        deployed = self.directory / 'BestestGameLive/data/data.json'
        deployed.parent.mkdir(parents=True)
        deployed.write_text('{"Tournaments": [{"Name": "Independent live deployment"}]}')
        self.config.write_text(json.dumps({'DatabasePath': str(self.development)}))
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.development.read_bytes(), deployed.read_bytes())
        self.assertEqual(self.live.read_bytes(), self.snapshot)

    def test_missing_deployed_database_does_not_fall_back_to_legacy_data(self):
        live_directory = self.directory / 'BestestGameLive'
        live_directory.mkdir()
        (live_directory / 'current').symlink_to(self.project)
        self.development.parent.mkdir()
        previous = b'{"debugging": "preserve this"}'
        self.development.write_bytes(previous)
        result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.development.read_bytes(), previous)

    def test_absolute_configured_live_path(self):
        self.config.write_text(json.dumps({'DatabasePath': str(self.live)}))
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.development.read_bytes(), self.snapshot)

    def test_missing_or_blank_configured_path_uses_project_database(self):
        (self.project / 'data.json').write_bytes(self.snapshot)
        for value in ({}, {'DatabasePath': ''}, {'DatabasePath': None}):
            with self.subTest(config=value):
                self.config.write_text(json.dumps(value))
                result = self.run_script()
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(self.development.read_bytes(), self.snapshot)

    def test_unreadable_or_invalid_live_data_preserves_existing_debug_data(self):
        self.development.parent.mkdir()
        previous = b'{"debugging": "keep this if preparation fails"}'
        self.development.write_bytes(previous)
        for invalid in (None, b'', b'{"Tournaments": [', b'null', b'[]'):
            with self.subTest(live=invalid):
                if invalid is None:
                    self.live.unlink()
                else:
                    self.live.write_bytes(invalid)
                result = self.run_script()
                self.assertNotEqual(result.returncode, 0)
                self.assertIn('Could not refresh the development database', result.stderr)
                self.assertEqual(self.development.read_bytes(), previous)
                self.assert_only_development_database()

    def test_development_symlink_to_live_database_is_rejected(self):
        self.development.parent.mkdir()
        self.development.symlink_to(self.live)
        result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('paths must be different', result.stderr)
        self.assertEqual(self.live.read_bytes(), self.snapshot)
        self.assertTrue(self.development.is_symlink())

    def test_failed_replacement_preserves_previous_data_and_cleans_temporary_file(self):
        self.development.parent.mkdir()
        previous = b'{"debugging": "previous data"}'
        self.development.write_bytes(previous)
        refresh = runpy.run_path(str(self.script))['refresh_development_data']
        with mock.patch('os.replace', side_effect=PermissionError('simulated copy failure')):
            with self.assertRaises(PermissionError):
                refresh(self.checkout)
        self.assertEqual(self.development.read_bytes(), previous)
        self.assertEqual(self.live.read_bytes(), self.snapshot)
        self.assert_only_development_database()

    def test_snapshot_retries_live_save_in_progress(self):
        read_snapshot = runpy.run_path(str(self.script))['read_live_snapshot']
        with mock.patch.object(Path, 'read_bytes', side_effect=[b'{', self.snapshot]):
            with mock.patch('time.sleep'):
                self.assertEqual(read_snapshot(self.live), self.snapshot)


if __name__ == '__main__':
    unittest.main()
