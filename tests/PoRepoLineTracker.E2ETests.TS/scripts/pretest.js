const { exec, spawn } = require('child_process');
const net = require('net');
const http = require('http');

function run(cmd) {
  return new Promise((resolve, reject) => {
    exec(cmd, { windowsHide: true }, (err, stdout, stderr) => {
      if (err) return reject({ err, stdout, stderr });
      resolve({ stdout, stderr });
    });
  });
}

async function ensureAzurite() {
  try {
    const { stdout } = await run('docker ps --filter ancestor=mcr.microsoft.com/azure-storage/azurite --format "{{.ID}}"');
    if (!stdout.trim()) {
      console.log('Azurite not running: starting container...');
      await run('docker run --rm -d -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite');
    } else {
      console.log('Azurite already running.');
    }
  } catch (e) {
    console.warn('Docker/azurite command failed:', e && e.err ? e.err.message : e);
    // don't fail hard - try to continue (local dev may not have docker)
  }
}

function waitForPort(host, port, timeoutMs = 30000) {
  return new Promise((resolve, reject) => {
    const start = Date.now();
    (function tryConnect() {
      const socket = net.connect(port, host);
      socket.on('connect', () => {
        socket.end();
        resolve(true);
      });
      socket.on('error', () => {
        socket.destroy();
        if (Date.now() - start > timeoutMs) return reject(new Error(`Timed out waiting for ${host}:${port}`));
        setTimeout(tryConnect, 1000);
      });
    })();
  });
}

function waitForHealth(url, timeoutMs = 60000) {
  return new Promise((resolve, reject) => {
    const start = Date.now();
    (function poll() {
      http.get(url, (res) => {
        if (res.statusCode >= 200 && res.statusCode < 300) return resolve(true);
        if (Date.now() - start > timeoutMs) return reject(new Error('Timed out waiting for health endpoint'));
        setTimeout(poll, 1000);
      }).on('error', () => {
        if (Date.now() - start > timeoutMs) return reject(new Error('Timed out waiting for health endpoint'));
        setTimeout(poll, 1000);
      });
    })();
  });
}

async function startApiDetached() {
  console.log('Starting API in detached background process...');
  const repoRoot = require('path').resolve(__dirname, '..', '..', '..');
  // Cross-platform detached start from repo root
  const isWin = process.platform === 'win32';
  if (isWin) {
    // Kill any process on port 5000, then start API in a detached PowerShell process with env vars set so they apply to the child
    const pwCommand = `$env:GITHUB__ClientId='test-client-id'; $env:GITHUB__ClientSecret='test-client-secret'; if (Get-NetTCPConnection -LocalPort 5000 -ErrorAction SilentlyContinue) { Get-NetTCPConnection -LocalPort 5000 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess | ForEach-Object { Stop-Process -Id $_ -Force } }; cd '${repoRoot}'; dotnet run --project src/PoRepoLineTracker.Api`;
    const args = ['-NoProfile', '-Command', pwCommand];
    return new Promise((resolve, reject) => {
      const p = spawn('powershell', args, { detached: true, stdio: 'ignore' });
      p.on('error', (e) => reject(e));
      p.unref();
      setTimeout(resolve, 1500);
    });
  } else {
    // For non-Windows, set env vars and start in background
    const args = ['-lc', `cd '${repoRoot}' && GITHUB__ClientId=test-client-id GITHUB__ClientSecret=test-client-secret dotnet run --project src/PoRepoLineTracker.Api &>/dev/null &`];
    return new Promise((resolve, reject) => {
      const p = spawn('bash', args, { detached: true, stdio: 'ignore' });
      p.on('error', (e) => reject(e));
      p.unref();
      setTimeout(resolve, 1500);
    });
  }
}

(async function main() {
  try {
    console.log('Running pretest: ensure Azurite + API are running and healthy');
    await ensureAzurite();

    // Wait for Azurite Table service 10002
    try {
      await waitForPort('127.0.0.1', 10002, 30000);
      console.log('Azurite port 10002 is available');
    } catch (e) {
      console.warn('Azurite did not appear on port 10002 within timeout (continuing):', e.message);
    }

    // Start API detached
    await startApiDetached();

    // Wait for API /health (allow more time for local startup)
    await waitForHealth('http://localhost:5000/health', 120000);
    console.log('API health endpoint is ready');
    process.exit(0);
  } catch (err) {
    console.error('Pretest failed:', err && err.message ? err.message : err);
    process.exit(1);
  }
})();
