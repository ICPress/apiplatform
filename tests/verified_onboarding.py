"""Staging integration test; requires an isolated MySQL database and stopped mail worker.
Set AUTH_TEST_API_URL, AUTH_TEST_MYSQL_JSON and AUTH_TEST_DISPOSABLE=1.
MYSQL_JSON is a JSON object accepted by pymysql.connect; install PyMySQL first.
Leaves uniquely named test fixtures for inspection. Never run against production.
"""
import concurrent.futures
import json
import os
import uuid
import urllib.request
import urllib.error


def main():
    assert os.environ.get('AUTH_TEST_DISPOSABLE') == '1', 'Use an isolated staging database with the mail worker stopped.'
    import pymysql
    api = os.environ['AUTH_TEST_API_URL'].rstrip('/')
    config = json.loads(os.environ['AUTH_TEST_MYSQL_JSON'])
    config['autocommit'] = True
    db = pymysql.connect(**config)
    tag = uuid.uuid4().hex[:12]
    checks = []

    def call(path, body=None, bearer=None):
        headers = {'Content-Type': 'application/json'}
        if bearer:
            headers['Authorization'] = 'Bearer ' + bearer
        request = urllib.request.Request(api + path, data=json.dumps(body).encode() if body is not None else None, headers=headers)
        try:
            response = urllib.request.urlopen(request, timeout=20)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            data = response.read().decode()
            try:
                data = json.loads(data)
            except ValueError:
                pass
            return response.status, data

    def scalar(sql, args):
        with db.cursor() as cursor:
            cursor.execute(sql, args)
            row = cursor.fetchone()
            return row[0] if row else None

    def email_proof(email):
        assert call('/Account/web/email', {'email': email})[0] == 202
        return scalar('SELECT additional_data FROM mail_queue WHERE email=%s ORDER BY created_at DESC LIMIT 1', (email,))

    def pending(email):
        link = email_proof(email)
        assert link
        assert scalar('SELECT COUNT(*) FROM users WHERE email=%s', (email,)) == 0
        assert call('/Account/web/complete', {'registrationToken': link, 'username': 'test_' + tag, 'fullName': 'Test User'})[0] == 401
        code, result = call('/Account/web/verify', {'token': link})
        assert code == 200 and result['requiresProfile'] is True
        assert 'token' not in result and result['email'] == email
        assert call('/Account/web/verify', {'token': link})[0] == 401
        assert scalar('SELECT COUNT(*) FROM users WHERE email=%s', (email,)) == 0
        return result['registrationToken']

    email = f'auth-test-{tag}@example.invalid'
    proof = pending(email)
    checks.append('email verified once; no user or session before profile completion')
    assert call('/Account/me', bearer=proof)[0] in (401, 403)
    assert call('/Account/web/verify', {'token': proof})[0] == 401
    head, payload, signature = proof.split('.')
    bad = head + '.' + payload + '.' + ('A' if signature[0] != 'A' else 'B') + signature[1:]
    body = {'registrationToken': proof, 'username': 'test_' + tag, 'fullName': 'Test Person'}
    assert call('/Account/web/complete', {**body, 'registrationToken': bad})[0] == 401
    assert call('/Account/web/complete', {**body, 'fullName': ' '})[0] == 400
    checks.append('proof cannot authenticate API, change purpose, be tampered with, or omit name')
    # The API must ignore a caller-supplied email and use the verified proof identity.
    body['email'] = 'attacker@example.invalid'
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(lambda _: call('/Account/web/complete', body), range(2)))
    assert sorted(code for code, _ in results) == [200, 401], [code for code, _ in results]
    token = next(result['token'] for code, result in results if code == 200)
    code, me = call('/Account/me', bearer=token)
    assert code == 200 and me['email'] == email and me['fullName'] == 'Test Person' and me['username'] == body['username']
    checks.append('simultaneous completion creates exactly one account with verified email')
    link = email_proof(email)
    code, result = call('/Account/web/verify', {'token': link})
    assert code == 200 and 'token' in result and not result.get('requiresProfile')
    checks.append('returning email user signs in without profile setup')
    # Browser has consumed its original email proof. Android gets a separate final proof.
    code, handoff = call('/Account/web/app-link', {}, bearer=result['token'])
    assert code == 200 and handoff['verificationToken'] != link
    app_proof = handoff['verificationToken']
    import hashlib
    lifetime = scalar('SELECT TIMESTAMPDIFF(SECOND, UTC_TIMESTAMP(), expires_at) FROM sent_links_users WHERE token_hash=%s', (hashlib.sha256(app_proof.encode()).digest(),))
    assert 0 < lifetime <= 300
    assert call('/Account/web/app-link', {}, bearer=proof)[0] in (401, 403)
    assert call('/Account/web/app-link', {})[0] in (401, 403)
    code, app_token = call('/Account/verify/' + app_proof, {})
    assert code == 200 and isinstance(app_token, str)
    assert call('/Account/me', bearer=app_token)[1]['email'] == email
    assert call('/Account/verify/' + app_proof, {})[0] == 401
    assert call('/Account/me', bearer=token)[0] == 200
    checks.append('independent five-minute Android proof redeems once; browser session remains valid')
    code, expiring = call('/Account/web/app-link', {}, bearer=token)
    assert code == 200
    with db.cursor() as cursor:
        cursor.execute('UPDATE sent_links_users SET expires_at=UTC_TIMESTAMP()-INTERVAL 1 SECOND WHERE token_hash=%s', (hashlib.sha256(expiring['verificationToken'].encode()).digest(),))
    assert call('/Account/verify/' + expiring['verificationToken'], {})[0] == 401
    checks.append('expired Android proof cannot be redeemed')
    second = pending(f'auth-test-{tag}-second@example.invalid')
    assert call('/Account/web/complete', {**body, 'registrationToken': second})[0] == 409
    code, result = call('/Account/web/complete', {**body, 'registrationToken': second, 'username': 'other_' + tag})
    assert code == 200 and 'token' in result
    checks.append('username collision rolls back proof consumption and permits retry')
    expired = pending(f'auth-test-{tag}-expired@example.invalid')
    import hashlib
    with db.cursor() as cursor:
        cursor.execute('UPDATE sent_links_users SET expires_at=UTC_TIMESTAMP()-INTERVAL 1 SECOND WHERE token_hash=%s', (hashlib.sha256(expired.encode()).digest(),))
    assert call('/Account/web/complete', {**body, 'registrationToken': expired, 'username': 'exp_' + tag})[0] == 401
    checks.append('database expiry prevents profile completion')
    db.close()
    print(json.dumps({'status': 'passed', 'checks': checks, 'fixture_tag': tag}, indent=2))


if __name__ == '__main__':
    main()
