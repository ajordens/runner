import * as glob from '@actions/glob'
import * as crypto from 'crypto'
import * as fs from 'fs'
import * as stream from 'stream'
import * as util from 'util'
import * as path from 'path'

async function run(): Promise<void> {
  // arg0 -> node
  // arg1 -> hashFiles.js
  // env[followSymbolicLinks] = true/null
  // env[pattern] -> glob pattern
  let followSymbolicLinks = false
  const matchPattern = process.env['pattern'] || ''
  if (process.env['followSymbolicLinks'] === 'true') {
    console.log('Follow symbolic links')
    followSymbolicLinks = true
  }

  console.log(`Match Pattern: ${matchPattern}`)
  let hasMatch = false
  const githubWorkspace = process.cwd()
  const result = crypto.createHash('sha256')
  const globber = await glob.create(matchPattern, {followSymbolicLinks})
  for await (const file of globber.globGenerator()) {
    if (!hasMatch) {
      hasMatch = true
    }
    console.log(file)
    if (!file.startsWith(`${githubWorkspace}${path.sep}`)) {
      console.log(`Ignore '${file}' since it is not under GITHUB_WORKSPACE.`)
      continue
    }
    const hash = crypto.createHash('sha256')
    const pipeline = util.promisify(stream.pipeline)
    await pipeline(fs.createReadStream(file), hash)
    result.write(hash.digest())
  }
  result.end()

  if (hasMatch) {
    console.error(`__OUTPUT__${result.digest('hex')}__OUTPUT__`)
  } else {
    console.error(`__OUTPUT____OUTPUT__`)
  }
}

run()
