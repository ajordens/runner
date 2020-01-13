import * as glob from '@actions/glob'
import * as crypto from 'crypto'
import * as fs from 'fs'
import * as stream from 'stream'
import * as util from 'util'

async function run(): Promise<void> {
  // arg0 -> node
  // arg1 -> hashFiles.js
  // arg2 -> glob options
  // arg3 -> glob patterns
  let followSymbolicLinks = false
  let matchPattern = process.argv[2]
  if (process.argv[2] === '--followSymbolicLinks') {
    console.log('Follow symbolic links')
    followSymbolicLinks = true
    matchPattern = process.argv[3]
  }

  console.log(`Match Pattern: ${matchPattern}`)
  let hasMatch = false
  const result = crypto.createHash('sha256')
  const globber = await glob.create(matchPattern, {followSymbolicLinks})
  for await (const file of globber.globGenerator()) {
    if (!hasMatch) {
      hasMatch = true
    }
    console.log(file)
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
