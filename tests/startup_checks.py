"""Exercise deployment transactions and development database snapshots."""

from contextlib import closing
import fcntl
import json
import os
from pathlib import Path
import runpy
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


REPOSITORY = Path(__file__).resolve().parents[1]


def write_database(path, marker):
    """A real SQLite file holding one recognisable value, standing in for the application's database."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.unlink(missing_ok=True)
    with closing(sqlite3.connect(path)) as database:
        database.execute('CREATE TABLE marker (value TEXT)')
        database.execute('INSERT INTO marker VALUES (?)', (marker,))
        database.commit()


def read_database(path):
    with closing(sqlite3.connect(f'{path.resolve().as_uri()}?mode=ro', uri=True)) as database:
        return database.execute('SELECT value FROM marker').fetchone()[0]
MOCK_COMMAND = r'''
import json, os, pathlib, sqlite3, sys
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
        # Only the release being deployed builds a database; the unit the update writes names its file.
        built = os.environ.get('BESTEST_TEST_APP_BUILDS_DATABASE')
        unit = pathlib.Path(os.environ['XDG_CONFIG_HOME'], 'systemd/user/bestestgame.service')
        if built and not os.path.exists(built) and unit.is_file() and built in unit.read_text():
            with sqlite3.connect(built) as database:
                database.execute("CREATE TABLE marker AS SELECT 'built by the new release' AS value")
            database.close()
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
        (project / 'appsettings.json').write_text(json.dumps({'DatabasePath': '../../Database/data.db'}))
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
        self.database = self.runtime / 'data/data.db'
        write_database(self.database, 'must survive updates')
        self.live_bytes = self.database.read_bytes()
        # Where a release from before SQLite kept the live data.
        self.earlier_json = self.runtime / 'data/data.json'
        self.json_snapshot = '{"live-data": "must survive updates"}\n'
        self.legacy_database = self.directory / 'Database/data.db'
        write_database(self.legacy_database, 'must survive updates')
        self.development_database = self.checkout / '.dev-data/data.db'
        write_database(self.development_database, 'never deploy this')
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
        self.assertEqual(self.database.read_bytes(), self.live_bytes)

    def use_live_json_from_before_sqlite(self):
        self.database.unlink()
        self.earlier_json.write_text(self.json_snapshot)

    def test_update_builds_before_stopping_and_preserves_data(self):
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotEqual((self.runtime / 'current').resolve(), self.previous)
        self.assertEqual((self.previous / 'BestestGame.dll').read_text(), 'old application')
        self.assertEqual(self.database.read_bytes(), self.live_bytes)
        backups = list((self.runtime / 'backups').glob('*.db'))
        self.assertEqual(len(backups), 1)
        self.assertEqual(read_database(backups[0]), 'must survive updates')
        self.assertEqual(sorted(path.name for path in self.database.parent.iterdir()), ['data.db'])
        self.assertEqual(read_database(self.development_database), 'never deploy this')
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
        self.assertEqual(len(list((self.runtime / 'backups').glob('*.db'))), 2)
        self.assertFalse(list((self.runtime / 'backups').glob('.copy-*')))

    def test_backup_includes_changes_still_in_the_write_ahead_log(self):
        # The service is stopped by now, but a release that was killed leaves its newest votes
        # in the log beside the database rather than in the file itself.
        with closing(sqlite3.connect(self.database)) as writer:
            writer.execute('PRAGMA journal_mode = WAL')
            writer.execute('PRAGMA wal_autocheckpoint = 0')
            writer.execute("UPDATE marker SET value = 'saved just before the update'")
            writer.commit()
            self.assertTrue(Path(f'{self.database}-wal').stat().st_size > 0)
            result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        backup, = (self.runtime / 'backups').glob('*.db')
        self.assertEqual(read_database(backup), 'saved just before the update')
        self.assertEqual(read_database(self.database), 'saved just before the update')

    def test_damaged_live_database_stops_the_update_and_restores_the_service(self):
        self.database.write_bytes(b'not a database ' * 400)
        self.live_bytes = self.database.read_bytes()
        result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assert_previous_running()
        self.assertFalse(list((self.runtime / 'backups').glob('*')))

    def test_first_sqlite_release_moves_the_live_json_file_in(self):
        self.use_live_json_from_before_sqlite()
        result = self.run_script(BESTEST_TEST_APP_BUILDS_DATABASE=str(self.database))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotEqual((self.runtime / 'current').resolve(), self.previous)
        self.assertIn(f'DatabasePath={self.database}', self.unit.read_text())
        self.assertEqual(read_database(self.database), 'built by the new release')
        # The file the previous release saved is kept, unchanged, with the backups.
        self.assertFalse(self.earlier_json.exists())
        kept, = (self.runtime / 'backups').glob('*.json')
        self.assertEqual(kept.read_text(), self.json_snapshot)
        self.assertFalse(list((self.runtime / 'backups').glob('*.db')))
        # From then on a deployment backs up the database like any other.
        second = self.run_script()
        self.assertEqual(second.returncode, 0, second.stderr)
        backup, = (self.runtime / 'backups').glob('*.db')
        self.assertEqual(read_database(backup), 'built by the new release')
        self.assertEqual(len(list((self.runtime / 'backups').glob('*.json'))), 1)

    def test_failed_first_sqlite_release_leaves_the_json_file_for_the_previous_release(self):
        for failure in ('BESTEST_TEST_START_FAIL', 'BESTEST_TEST_HTTP_FAIL'):
            with self.subTest(failure=failure):
                self.database.unlink(missing_ok=True)
                self.earlier_json.write_text(self.json_snapshot)
                self.state_path.write_text(json.dumps({'active': True, 'enabled': True}))
                result = self.run_script(BESTEST_TEST_APP_BUILDS_DATABASE=str(self.database), **{failure: '1'})
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual((self.runtime / 'current').resolve(), self.previous)
                self.assertEqual(self.unit.read_text(), self.previous_unit)
                self.assertTrue(json.loads(self.state_path.read_text())['active'])
                self.assertEqual(self.earlier_json.read_text(), self.json_snapshot)
                # The database the failed release built is set aside, so the next attempt cannot
                # mistake it for current data after the previous release has saved more votes.
                self.assertFalse(self.database.exists())
                self.assertEqual(len(list((self.runtime / 'backups').glob('failed-migration-*.db'))), 1)
                shutil.rmtree(self.runtime / 'backups')
        self.earlier_json.write_text('{"live-data": "a vote saved after the failed update"}\n')
        result = self.run_script(BESTEST_TEST_APP_BUILDS_DATABASE=str(self.database))
        self.assertEqual(result.returncode, 0, result.stderr)
        kept, = (self.runtime / 'backups').glob('data-*.json')
        self.assertEqual(kept.read_text(), '{"live-data": "a vote saved after the failed update"}\n')
        self.assertTrue(self.database.exists())

    def test_json_file_newer_than_the_database_stops_the_update(self):
        # Left by going back to a release from before SQLite: its votes are in the JSON file only.
        self.earlier_json.write_text(self.json_snapshot)
        later = self.database.stat().st_mtime + 60
        os.utime(self.earlier_json, (later, later))
        result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('newer data.json', result.stderr)
        self.assert_previous_running()
        self.assertEqual(self.earlier_json.read_text(), self.json_snapshot)
        self.assertFalse(any(command['command'] == 'dotnet' for command in self.commands()))
        # An older JSON file beside the database is only a leftover and is set aside.
        earlier = self.database.stat().st_mtime - 60
        os.utime(self.earlier_json, (earlier, earlier))
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.database.read_bytes(), self.live_bytes)
        self.assertFalse(self.earlier_json.exists())
        self.assertEqual(len(list((self.runtime / 'backups').glob('data-*.json'))), 1)

    def test_json_file_stays_in_place_until_the_new_release_has_its_database(self):
        self.use_live_json_from_before_sqlite()
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.earlier_json.read_text(), self.json_snapshot)
        self.assertFalse(list((self.runtime / 'backups').glob('*.json')))

    def test_update_backs_up_uploaded_cover_pictures_only(self):
        covers = self.database.parent / 'covers'
        covers.mkdir()
        (covers / 'entry.upload-1a.png').write_bytes(b'uploaded by hand')
        (covers / 'entry-co1x78.jpg').write_bytes(b'fetched, can be downloaded again')
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        saved = self.runtime / 'backups/covers'
        self.assertEqual([path.name for path in saved.iterdir()], ['entry.upload-1a.png'])
        self.assertEqual((saved / 'entry.upload-1a.png').read_bytes(), b'uploaded by hand')
        self.assertEqual(sorted(path.name for path in covers.iterdir()), ['entry-co1x78.jpg', 'entry.upload-1a.png'])
        # A picture removed from the live folder stays in the backup; a new one joins it.
        (covers / 'entry.upload-1a.png').unlink()
        (covers / 'other.upload-2b.webp').write_bytes(b'second upload')
        second = self.run_script()
        self.assertEqual(second.returncode, 0, second.stderr)
        self.assertEqual(sorted(path.name for path in saved.iterdir()), ['entry.upload-1a.png', 'other.upload-2b.webp'])
        self.assertEqual((saved / 'entry.upload-1a.png').read_bytes(), b'uploaded by hand')

    def test_update_without_uploaded_covers_creates_no_cover_backup(self):
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse((self.runtime / 'backups/covers').exists())

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
        legacy_bytes = self.legacy_database.read_bytes()
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue((self.runtime / 'current').is_symlink())
        self.assertIn(str(self.database), self.unit.read_text())
        self.assertEqual(read_database(self.database), 'must survive updates')
        self.assertFalse(os.path.samefile(self.database, self.legacy_database))
        self.assertEqual(self.legacy_database.read_bytes(), legacy_bytes)

    def test_legacy_json_service_seeds_the_live_folder_for_the_new_release(self):
        (self.runtime / 'current').unlink()
        self.database.unlink()
        self.legacy_database.unlink()
        legacy_json = self.legacy_database.with_suffix('.json')
        legacy_json.write_text(self.json_snapshot)
        result = self.run_script(BESTEST_TEST_APP_BUILDS_DATABASE=str(self.database))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(legacy_json.read_text(), self.json_snapshot)
        self.assertEqual(read_database(self.database), 'built by the new release')
        self.assertFalse(self.earlier_json.exists())
        kept, = (self.runtime / 'backups').glob('*.json')
        self.assertEqual(kept.read_text(), self.json_snapshot)

    def test_first_install_without_earlier_data_starts_empty(self):
        (self.runtime / 'current').unlink()
        self.database.unlink()
        self.legacy_database.unlink()
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(list(self.database.parent.iterdir()), [])

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
        self.assertEqual(read_database(self.legacy_database), 'must survive updates')

        # A retry must copy the latest legacy data, not reuse the failed attempt's snapshot.
        write_database(self.legacy_database, 'new score after failed migration')
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(read_database(self.database), 'new score after failed migration')

    def test_failed_legacy_json_migration_restores_source_service(self):
        (self.runtime / 'current').unlink()
        self.database.unlink()
        self.legacy_database.unlink()
        legacy_json = self.legacy_database.with_suffix('.json')
        legacy_json.write_text(self.json_snapshot)
        result = self.run_script(BESTEST_TEST_START_FAIL='1', BESTEST_TEST_APP_BUILDS_DATABASE=str(self.database))
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(list(self.database.parent.iterdir()), [])
        self.assertEqual(legacy_json.read_text(), self.json_snapshot)

        legacy_json.write_text('{"live-data": "new score after failed migration"}\n')
        result = self.run_script(BESTEST_TEST_APP_BUILDS_DATABASE=str(self.database))
        self.assertEqual(result.returncode, 0, result.stderr)
        kept, = (self.runtime / 'backups').glob('data-*.json')
        self.assertEqual(kept.read_text(), '{"live-data": "new score after failed migration"}\n')

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
        write_database(self.legacy_database, 'do not deploy this')
        (self.checkout / 'BestestGame/appsettings.json').write_text(json.dumps({
            'DatabasePath': str(self.development_database),
        }))
        result = self.run_script(args=['--wait'])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.database.read_bytes(), self.live_bytes)

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
        self.assertEqual(read_database(self.development_database), 'must survive updates')
        self.assertEqual(self.database.read_bytes(), self.live_bytes)
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
        self.config.write_text(json.dumps({'DatabasePath': '../../Database/data.db'}))
        self.script = self.checkout / 'refresh-dev-data.py'
        shutil.copy2(REPOSITORY / 'refresh-dev-data.py', self.script)
        self.live = self.directory / 'Database/data.db'
        write_database(self.live, 'Live tournament')
        self.live_bytes = self.live.read_bytes()
        self.development = self.checkout / '.dev-data/data.db'
        # A live application from before SQLite keeps a JSON file, which is copied as it is.
        self.json_snapshot = b'{"Tournaments": [{"Name": "Live tournament"}], "Duels": []}\n'
        self.development_json = self.development.with_suffix('.json')

    def run_script(self):
        return subprocess.run([sys.executable, str(self.script)], cwd=self.directory,
                              text=True, capture_output=True, timeout=5)

    def assert_only_development_database(self, database=None):
        self.assertEqual(list(self.development.parent.iterdir()), [database or self.development])

    def test_creates_independent_copy_and_resets_previous_debug_changes(self):
        for attempt in range(2):
            result = self.run_script()
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(read_database(self.development), 'Live tournament')
            self.assertFalse(os.path.samefile(self.live, self.development))
            self.assert_only_development_database()
            # A debug session changes its copy and, when stopped abruptly, leaves a write-ahead
            # log that must not be applied to the next copy.
            write_database(self.development, 'changed while debugging')
            Path(f'{self.development}-wal').write_bytes(b'left by the previous debug session')
            Path(f'{self.development}-shm').write_bytes(b'left by the previous debug session')
            self.development_json.write_bytes(b'{"debugging": "from before SQLite"}')
            self.assertEqual(self.live.read_bytes(), self.live_bytes)

    def test_refresh_mirrors_live_cover_pictures(self):
        live_covers = self.live.parent / 'covers'
        live_covers.mkdir()
        (live_covers / 'kept.jpg').write_bytes(b'live picture')
        (live_covers / 'entry.upload-1a.png').write_bytes(b'uploaded in the live application')
        development_covers = self.development.parent / 'covers'
        development_covers.mkdir(parents=True)
        (development_covers / 'kept.jpg').write_bytes(b'stale')
        (development_covers / 'debugging-only.jpg').write_bytes(b'left by discarded debugging changes')
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual({path.name: path.read_bytes() for path in development_covers.iterdir()},
                         {'kept.jpg': b'live picture', 'entry.upload-1a.png': b'uploaded in the live application'})
        self.assertEqual(sorted(path.name for path in live_covers.iterdir()), ['entry.upload-1a.png', 'kept.jpg'])
        self.assertEqual(read_database(self.development), 'Live tournament')
        # Without a live covers folder the debugging pictures are left alone.
        shutil.rmtree(live_covers)
        self.assertEqual(self.run_script().returncode, 0)
        self.assertEqual(len(list(development_covers.iterdir())), 2)

    def test_each_refresh_uses_current_live_data(self):
        self.assertEqual(self.run_script().returncode, 0)
        write_database(self.live, 'New live tournament')
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(read_database(self.development), 'New live tournament')

    def test_refresh_reads_votes_the_running_live_application_has_not_folded_into_its_file(self):
        with closing(sqlite3.connect(self.live)) as application:
            application.execute('PRAGMA journal_mode = WAL')
            application.execute('PRAGMA wal_autocheckpoint = 0')
            application.execute("UPDATE marker SET value = 'voted a moment ago'")
            application.commit()
            self.assertTrue(Path(f'{self.live}-wal').stat().st_size > 0)
            result = self.run_script()
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(read_database(self.development), 'voted a moment ago')
            self.assert_only_development_database()
            # The live application carries on writing afterwards.
            application.execute("UPDATE marker SET value = 'and voted again'")
            application.commit()
        self.assertEqual(read_database(self.live), 'and voted again')

    def test_refresh_uses_sibling_live_database_instead_of_source_configuration(self):
        deployed = self.directory / 'BestestGameLive/data/data.db'
        write_database(deployed, 'Independent live deployment')
        # Left with the live data by mistake, a JSON file from before SQLite is not what is live.
        deployed.with_suffix('.json').write_bytes(self.json_snapshot)
        self.config.write_text(json.dumps({'DatabasePath': str(self.development)}))
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(read_database(self.development), 'Independent live deployment')
        self.assert_only_development_database()
        self.assertEqual(self.live.read_bytes(), self.live_bytes)

    def test_live_release_from_before_sqlite_is_copied_as_its_json_file(self):
        deployed = self.directory / 'BestestGameLive/data/data.json'
        deployed.parent.mkdir(parents=True)
        deployed.write_bytes(self.json_snapshot)
        # The database the previous debug session built from an older copy would otherwise be kept.
        write_database(self.development, 'built from an older copy')
        Path(f'{self.development}-wal').write_bytes(b'left by the previous debug session')
        for attempt in range(2):
            result = self.run_script()
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(self.development_json.read_bytes(), self.json_snapshot)
            self.assert_only_development_database(self.development_json)
            self.assertEqual(deployed.read_bytes(), self.json_snapshot)

    def test_source_service_from_before_sqlite_is_copied_as_its_json_file(self):
        self.live.unlink()
        self.live.with_suffix('.json').write_bytes(self.json_snapshot)
        for configured in ('../../Database/data.db', '../../Database/data.json'):
            with self.subTest(configured=configured):
                self.config.write_text(json.dumps({'DatabasePath': configured}))
                result = self.run_script()
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(self.development_json.read_bytes(), self.json_snapshot)
                self.assert_only_development_database(self.development_json)

    def test_missing_deployed_database_does_not_fall_back_to_legacy_data(self):
        live_directory = self.directory / 'BestestGameLive'
        live_directory.mkdir()
        (live_directory / 'current').symlink_to(self.project)
        write_database(self.development, 'preserve this')
        previous = self.development.read_bytes()
        result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.development.read_bytes(), previous)

    def test_absolute_configured_live_path(self):
        self.config.write_text(json.dumps({'DatabasePath': str(self.live)}))
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(read_database(self.development), 'Live tournament')

    def test_missing_or_blank_configured_path_uses_project_database(self):
        write_database(self.project / 'data.db', 'Beside the project')
        for value in ({}, {'DatabasePath': ''}, {'DatabasePath': None}):
            with self.subTest(config=value):
                self.config.write_text(json.dumps(value))
                result = self.run_script()
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(read_database(self.development), 'Beside the project')

    def test_unreadable_or_invalid_live_data_preserves_existing_debug_data(self):
        write_database(self.development, 'keep this if preparation fails')
        previous = self.development.read_bytes()
        with closing(sqlite3.connect(self.live)) as database:
            database.executemany('INSERT INTO marker VALUES (?)', [(f'row {index}' * 50,) for index in range(400)])
            database.commit()
        truncated = self.live.read_bytes()[:-8192]
        for invalid in (None, b'not a database ' * 400, truncated, b'{"Tournaments": []}'):
            with self.subTest(live=invalid and invalid[:20]):
                if invalid is None:
                    self.live.unlink()
                else:
                    self.live.write_bytes(invalid)
                result = self.run_script()
                self.assertNotEqual(result.returncode, 0)
                self.assertIn('Could not refresh the development database', result.stderr)
                self.assertEqual(self.development.read_bytes(), previous)
                self.assert_only_development_database()

    def test_invalid_live_json_preserves_existing_debug_data(self):
        deployed = self.directory / 'BestestGameLive/data/data.json'
        deployed.parent.mkdir(parents=True)
        self.development.parent.mkdir()
        previous = b'{"debugging": "keep this if preparation fails"}'
        self.development_json.write_bytes(previous)
        for invalid in (b'', b'{"Tournaments": [', b'null', b'[]'):
            with self.subTest(live=invalid):
                deployed.write_bytes(invalid)
                result = self.run_script()
                self.assertNotEqual(result.returncode, 0)
                self.assertIn('Could not refresh the development database', result.stderr)
                self.assertEqual(self.development_json.read_bytes(), previous)
                self.assert_only_development_database(self.development_json)

    def test_development_symlink_to_live_database_is_rejected(self):
        self.development.parent.mkdir()
        self.development.symlink_to(self.live)
        result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('paths must be different', result.stderr)
        self.assertEqual(self.live.read_bytes(), self.live_bytes)
        self.assertTrue(self.development.is_symlink())

    def test_failed_replacement_preserves_previous_data_and_cleans_temporary_file(self):
        write_database(self.development, 'previous data')
        previous = self.development.read_bytes()
        refresh = runpy.run_path(str(self.script))['refresh_development_data']
        with mock.patch('os.replace', side_effect=PermissionError('simulated copy failure')):
            with self.assertRaises(PermissionError):
                refresh(self.checkout)
        self.assertEqual(self.development.read_bytes(), previous)
        self.assertEqual(self.live.read_bytes(), self.live_bytes)
        self.assert_only_development_database()

    def test_snapshot_retries_live_save_in_progress(self):
        read_snapshot = runpy.run_path(str(self.script))['read_live_snapshot']
        with mock.patch.object(Path, 'read_bytes', side_effect=[b'{', self.json_snapshot]):
            with mock.patch('time.sleep'):
                self.assertEqual(read_snapshot(self.live), self.json_snapshot)


if __name__ == '__main__':
    unittest.main()
