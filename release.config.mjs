/**
 * semantic-release configuration.
 *
 * - `main` publishes stable versions (vX.Y.Z); `next` publishes pre-releases (vX.Y.Z-pre.N).
 * - Only a tag and a GitHub release are created; nothing is committed back to the repository.
 * - The new tag is written to the step's GITHUB_OUTPUT so release.yml can call docker.yml for it
 *   (tags pushed with GITHUB_TOKEN do not trigger other workflows); docker.yml appends the image
 *   details to the release notes.
 */
export default {
  branches: ['main', { name: 'next', prerelease: 'pre' }],
  tagFormat: 'v${version}',
  plugins: [
    ['@semantic-release/commit-analyzer', { preset: 'conventionalcommits' }],
    ['@semantic-release/release-notes-generator', { preset: 'conventionalcommits' }],
    [
      '@semantic-release/github',
      { successComment: false, failComment: false, releasedLabels: false },
    ],
    [
      '@semantic-release/exec',
      { successCmd: 'echo "tag=${nextRelease.gitTag}" >> "$GITHUB_OUTPUT"' },
    ],
  ],
};
